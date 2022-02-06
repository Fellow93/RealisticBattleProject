using HarmonyLib;
using RealisticBattleAiModule.AiModule.RbmBehaviors;
using SandBox;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ItemObject;

namespace RealisticBattleAiModule
{
    class Tactics
    {
        private static bool carryOutDefenceEnabled = true;
        private static bool archersShiftAroundEnabled = true;
        private static bool balanceLaneDefendersEnabled = true;

        public class AIDecision
        {
            public int cooldown = 0;
            public WorldPosition position = WorldPosition.Invalid;
        }

        public static Dictionary<Agent, AIDecision> aiDecisionCooldownDict = new Dictionary<Agent, AIDecision>();

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
                    if (formation.QuerySystem.IsCavalryFormation && !Utilities.CheckIfMountedSkirmishFormation(formation, 0.6f))
                    {
                        return TargetIconType.Cavalry_Light.ToString();
                    }
                    if (formation.QuerySystem.IsCavalryFormation && Utilities.CheckIfMountedSkirmishFormation(formation, 0.6f))
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

        [HarmonyPatch(typeof(CampaignMissionComponent))]
        [HarmonyPatch("EarlyStart")]
        public class CampaignMissionComponentPatch
        {
            public static void Postfix()
            {
                MyPatcher.DoPatching();
            }
        }

        [HarmonyPatch(typeof(MissionCombatantsLogic))]
        [HarmonyPatch("EarlyStart")]
        public class TeamAiFieldBattle
        {
            public static void Postfix()
            {
                carryOutDefenceEnabled = true;
                archersShiftAroundEnabled = true;
                aiDecisionCooldownDict.Clear();
                MyPatcher.DoPatching();
                OnTickAsAIPatch.itemPickupDistanceStorage.Clear();
                ManagedParameters.SetParameter(ManagedParametersEnum.BipedalRadius, 0.5f);
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
                                //team.AddTacticOption(new TacticFullScaleAttackWithDedicatedSkirmishers(team));
                            }
                            if (team.Side == BattleSideEnum.Defender)
                            {
                                team.ClearTacticOptions();
                                team.AddTacticOption(new TacticDefensiveEngagement(team));
                                team.AddTacticOption(new TacticDefensiveLine(team));
                                team.AddTacticOption(new TacticFullScaleAttack(team));
                                //team.AddTacticOption(new TacticCharge(team));
                                team.AddTacticOption(new TacticRangedHarrassmentOffensive(team));
                                //team.AddTacticOption(new TacticHoldChokePoint(team));
                                //team.AddTacticOption(new TacticHoldTheHill(team));
                                //team.AddTacticOption(new TacticRangedHarrassmentOffensive(team));
                                team.AddTacticOption(new TacticCoordinatedRetreat(team));
                                //team.AddTacticOption(new TacticFrontalCavalryCharge(team));
                                //team.AddTacticOption(new TacticDefensiveRing(team));
                                //team.AddTacticOption(new TacticArchersOnTheHill(team));
                            }
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Mission))]
        class SpawnTroopPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("SpawnTroop")]
            static bool PrefixSpawnTroop(ref Mission __instance, IAgentOriginBase troopOrigin, bool isPlayerSide, bool hasFormation, bool spawnWithHorse, bool isReinforcement, bool enforceSpawningOnInitialPoint, int formationTroopCount, int formationTroopIndex, bool isAlarmed, bool wieldInitialWeapons, bool forceDismounted,ref Vec3? initialPosition,ref Vec2? initialDirection)
            {
                if(Mission.Current != null && Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle)
                {
                    if (isReinforcement)
                    {
                        if (hasFormation)
                        {
                            BasicCharacterObject troop = troopOrigin.Troop;
                            Team agentTeam = Mission.GetAgentTeam(troopOrigin, isPlayerSide);
                            Formation formation = agentTeam.GetFormation(troop.GetFormationClass(troopOrigin.BattleCombatant));
                            if(formation.CountOfUnits == 0)
                            {
                                foreach(Formation allyFormation in agentTeam.Formations)
                                {
                                    if(allyFormation.CountOfUnits > 0)
                                    {
                                        formation = allyFormation;
                                        break;
                                    }
                                }
                            }
                            if (formation.CountOfUnits == 0)
                            {
                                return true;
                            }
                            Vec2 tempPos = Mission.Current.GetClosestFleePositionForFormation(formation).AsVec2;
                            tempPos.x = tempPos.x + MBRandom.RandomInt(40);
                            tempPos.y = tempPos.y + MBRandom.RandomInt(40);

                            initialPosition = Mission.Current.GetClosestDeploymentBoundaryPosition(agentTeam.Side, tempPos).ToVec3();
                            initialDirection = tempPos - formation.CurrentPosition;
                        }
                    }
                }
                return true;
            }

        }

        [HarmonyPatch(typeof(TacticDefendCastle))]
        class TacticDefendCastlePatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("CarryOutDefense")]
            static bool PrefixCarryOutDefense(ref TacticDefendCastle __instance, ref bool doRangedJoinMelee)
            {
                if (carryOutDefenceEnabled)
                {
                    carryOutDefenceEnabled = false;
                    doRangedJoinMelee = false;
                    return true;
                }
                else
                {
                    return false;
                }
            }

            [HarmonyPrefix]
            [HarmonyPatch("ArcherShiftAround")]
            static bool PrefixArcherShiftAround(ref TacticDefendCastle __instance)
            {
                if (archersShiftAroundEnabled)
                {
                    archersShiftAroundEnabled = false;
                    return true;
                }
                else
                {
                    return false;
                }
            }

            [HarmonyPrefix]
            [HarmonyPatch("BalanceLaneDefenders")]
            static bool PrefixBalanceLaneDefenders(ref TacticDefendCastle __instance)
            {
                if (balanceLaneDefendersEnabled)
                {
                    balanceLaneDefendersEnabled = false;
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        [HarmonyPatch(typeof(Mission))]
        class MissionPatch
        {
            [HarmonyPostfix]
            [HarmonyPatch("OnObjectDisabled")]
            static void PostfixOnObjectDisabled(DestructableComponent destructionComponent)
            {
                if(destructionComponent.GameEntity.GetFirstScriptOfType<UsableMachine>() != null && destructionComponent.GameEntity.GetFirstScriptOfType<UsableMachine>().IsDestroyed)
                {
                    balanceLaneDefendersEnabled = true;
                    carryOutDefenceEnabled = true;
                }
            }
        }

        [HarmonyPatch(typeof(TacticCoordinatedRetreat))]
        class OverrideTacticCoordinatedRetreat
        {
            [HarmonyPrefix]
            [HarmonyPatch("GetTacticWeight")]
            static bool PrefixGetTacticWeight(ref TacticCoordinatedRetreat __instance, ref Team ___team, ref float __result)
            {
                //__result = 100f;
                if (___team.QuerySystem.InfantryRatio == 0f && ___team.QuerySystem.RangedRatio == 0f)
                {
                    float power = ___team.QuerySystem.TeamPower;
                    float enemyPower = ___team.QuerySystem.EnemyTeams.Sum((TeamQuerySystem et) => et.TeamPower);
                    if (power / enemyPower <= 0.10f)
                    {
                        __result = 1000f;
                    }
                    else
                    {
                        __result = 0f;
                    }
                }
                else
                {
                    __result = 0f;
                }
                return false;
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
                    ____rightCavalry.AI.ResetBehaviorWeights();
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = FormationAI.BehaviorSide.Right;
                    ____rightCavalry.AI.SetBehaviorWeight<RBMBehaviorForwardSkirmish>(1f).FlankSide = FormationAI.BehaviorSide.Right;
                }
                if (____leftCavalry != null)
                {
                    ____leftCavalry.AI.ResetBehaviorWeights();
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = FormationAI.BehaviorSide.Left;
                    ____leftCavalry.AI.SetBehaviorWeight<RBMBehaviorForwardSkirmish>(1f).FlankSide = FormationAI.BehaviorSide.Left;
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch("Attack")]
            static void PostfixAttack(ref Formation ____mainInfantry, ref Formation ____archers, ref Formation ____rightCavalry, ref Formation ____leftCavalry)
            {

                if (____archers != null)
                {
                    ____archers.AI.ResetBehaviorWeights();
                    ____archers.AI.SetBehaviorWeight<RBMBehaviorArcherSkirmish>(1f);
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(0f);
                }
                if (____rightCavalry != null)
                {
                    ____rightCavalry.AI.ResetBehaviorWeights();
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                    //____rightCavalry.AI.SetBehaviorWeight<RBMBehaviorForwardHorseSkirmish>(1f);
                    //____rightCavalry.AI.SetBehaviorWeight<BehaviorTacticalCharge>(0f);
                    //____rightCavalry.AI.SetBehaviorWeight<BehaviorPullBack>(0f);
                }
                if (____leftCavalry != null)
                {
                    ____leftCavalry.AI.ResetBehaviorWeights();
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                    //____leftCavalry.AI.SetBehaviorWeight<RBMBehaviorForwardHorseSkirmish>(1f);
                    //____leftCavalry.AI.SetBehaviorWeight<BehaviorTacticalCharge>(0f);
                    //____leftCavalry.AI.SetBehaviorWeight<BehaviorPullBack>(0f);
                }

                Utilities.FixCharge(ref ____mainInfantry);
            }

            [HarmonyPostfix]
            [HarmonyPatch("HasBattleBeenJoined")]
            static void PostfixHasBattleBeenJoined(Formation ____mainInfantry, bool ____hasBattleBeenJoined, ref bool __result)
            {
                __result = Utilities.HasBattleBeenJoined( ____mainInfantry, ____hasBattleBeenJoined);
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
                    ____leftCavalry.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                    {
                        bool ismountedSkrimisher = false;
                        for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                        {
                            if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                            {
                                if (agent.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Thrown && agent.MountAgent != null)
                                {
                                    ismountedSkrimisher = true;
                                    break;
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

                    ____rightCavalry.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                    {
                        bool ismountedSkrimisher = false;
                        for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                        {
                            if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                            {
                                if (agent.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Thrown && agent.MountAgent != null)
                                {
                                    ismountedSkrimisher = true;
                                    break;
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
                    ____cavalry.AI.SetBehaviorWeight<RBMBehaviorForwardSkirmish>(1f);
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
                    ____cavalry.AI.SetBehaviorWeight<RBMBehaviorForwardSkirmish>(1f);
                    //____cavalry.AI.SetBehaviorWeight<BehaviorTacticalCharge>(0f);
                    //____cavalry.AI.SetBehaviorWeight<BehaviorPullBack>(0f);
                }
                if (____archers != null)
                {
                    ____archers.AI.ResetBehaviorWeights();
                    ____archers.AI.SetBehaviorWeight<RBMBehaviorArcherSkirmish>(1f);
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(0f);
                }
                Utilities.FixCharge(ref ____mainInfantry);
            }

            [HarmonyPostfix]
            [HarmonyPatch("HasBattleBeenJoined")]
            static void PostfixHasBattleBeenJoined(Formation ____cavalry, bool ____hasBattleBeenJoined, ref bool __result)
            {
                __result = Utilities.HasBattleBeenJoined( ____cavalry, ____hasBattleBeenJoined, 125f);
            }

            //[HarmonyPostfix]
            //[HarmonyPatch("GetTacticWeight")]
            //static void PostfixGetAiWeight(TacticFrontalCavalryCharge __instance, ref float __result)
            //{
            //    FieldInfo teamField = typeof(TacticFrontalCavalryCharge).GetField("team", BindingFlags.NonPublic | BindingFlags.Instance);
            //    teamField.DeclaringType.GetField("team");
            //    Team currentTeam = (Team)teamField.GetValue(__instance);
            //        if (currentTeam.QuerySystem.CavalryRatio > 0.1f)
            //        {
            //            __result = currentTeam.QuerySystem.CavalryRatio * 4f;
            //        }
            //}
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
                    ____rightCavalry.AI.SetBehaviorWeight<RBMBehaviorForwardSkirmish>(1f).FlankSide = FormationAI.BehaviorSide.Right;
                }
                if (____leftCavalry != null)
                {
                    ____leftCavalry.AI.SetBehaviorWeight<RBMBehaviorForwardSkirmish>(1f).FlankSide = FormationAI.BehaviorSide.Left;
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
                    //____rightCavalry.AI.SetBehaviorWeight<RBMBehaviorForwardHorseSkirmish>(1f);
                    //____rightCavalry.AI.SetBehaviorWeight<BehaviorTacticalCharge>(0f);
                    //____rightCavalry.AI.SetBehaviorWeight<BehaviorPullBack>(0f);
                }
                if (____leftCavalry != null)
                {
                    ____leftCavalry.AI.ResetBehaviorWeights();
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                    //____leftCavalry.AI.SetBehaviorWeight<RBMBehaviorForwardHorseSkirmish>(1f);
                    //____leftCavalry.AI.SetBehaviorWeight<BehaviorTacticalCharge>(0f);
                    //____leftCavalry.AI.SetBehaviorWeight<BehaviorPullBack>(0f);
                }
                if (____archers != null)
                {
                    ____archers.AI.ResetBehaviorWeights();
                    ____archers.AI.SetBehaviorWeight<RBMBehaviorArcherSkirmish>(1f);
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(0f);
                }
                Utilities.FixCharge(ref ____mainInfantry);
            }

            [HarmonyPostfix]
            [HarmonyPatch("HasBattleBeenJoined")]
            static void PostfixHasBattleBeenJoined(Formation ____mainInfantry, bool ____hasBattleBeenJoined, ref bool __result)
            {
                __result = Utilities.HasBattleBeenJoined( ____mainInfantry, ____hasBattleBeenJoined);
            }

            [HarmonyPostfix]
            [HarmonyPatch("ManageFormationCounts")]
            static void PostfixManageFormationCounts(ref Formation ____leftCavalry, ref Formation ____rightCavalry)
            {
                if (____leftCavalry != null && ____rightCavalry != null)
                {
                    List<Agent> mountedSkirmishersList = new List<Agent>();
                    List<Agent> mountedMeleeList = new List<Agent>();
                    ____leftCavalry.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                    {
                        bool ismountedSkrimisher = false;
                        for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                        {
                            if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                            {
                                if (agent.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Thrown && agent.MountAgent != null)
                                {
                                    ismountedSkrimisher = true;
                                    break;
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

                    ____rightCavalry.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                    {
                        bool ismountedSkrimisher = false;
                        for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                        {
                            if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                            {
                                if (agent.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Thrown && agent.MountAgent != null)
                                {
                                    ismountedSkrimisher = true;
                                    break;
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
                __result = Utilities.HasBattleBeenJoined( ____mainInfantry, ____hasBattleBeenJoined);
                return false;
            }

            [HarmonyPostfix]
            [HarmonyPatch("Defend")]
            static void PostfixDefend(ref Formation ____archers, ref Formation ____mainInfantry, ref Formation ____rightCavalry, ref Formation ____leftCavalry)
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
            }

            [HarmonyPostfix]
            [HarmonyPatch("Engage")]
            static void PostfixEngage(ref Formation ____archers, ref Formation ____mainInfantry, ref Formation ____rightCavalry, ref Formation ____leftCavalry)
            {
                if (____archers != null)
                {
                    ____archers.AI.ResetBehaviorWeights();
                    ____archers.AI.SetBehaviorWeight<RBMBehaviorArcherSkirmish>(1f);
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(0f);
                }
                if (____rightCavalry != null)
                {
                    ____rightCavalry.AI.ResetBehaviorWeights();
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                    //____rightCavalry.AI.SetBehaviorWeight<RBMBehaviorForwardHorseSkirmish>(1f);
                    //____rightCavalry.AI.SetBehaviorWeight<BehaviorTacticalCharge>(0f);
                    //____rightCavalry.AI.SetBehaviorWeight<BehaviorPullBack>(0f);
                }
                if (____leftCavalry != null)
                {
                    ____leftCavalry.AI.ResetBehaviorWeights();
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                    //____leftCavalry.AI.SetBehaviorWeight<RBMBehaviorForwardHorseSkirmish>(1f);
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
                __result = Utilities.HasBattleBeenJoined(____mainInfantry, ____hasBattleBeenJoined);
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
                    ____archers.AI.ResetBehaviorWeights();
                    ____archers.AI.SetBehaviorWeight<RBMBehaviorArcherSkirmish>(1f);
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(0f);
                }
                if (____rightCavalry != null)
                {
                    ____rightCavalry.AI.ResetBehaviorWeights();
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                    //____rightCavalry.AI.SetBehaviorWeight<RBMBehaviorForwardHorseSkirmish>(1f);
                    //____rightCavalry.AI.SetBehaviorWeight<BehaviorTacticalCharge>(0f);
                    //____rightCavalry.AI.SetBehaviorWeight<BehaviorPullBack>(0f);
                }
                if (____leftCavalry != null)
                {
                    ____leftCavalry.AI.ResetBehaviorWeights();
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                    //____leftCavalry.AI.SetBehaviorWeight<RBMBehaviorForwardHorseSkirmish>(1f);
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
                        f.AI.SetBehaviorWeight<BehaviorCharge>(0.2f);
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