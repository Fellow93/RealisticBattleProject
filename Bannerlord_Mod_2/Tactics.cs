using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ItemObject;

namespace RealisticBattle
{
    class Tactics
    {

        [HarmonyPatch(typeof(Team))]
        [HarmonyPatch("Tick")]
        class OverrideTick
        {

            static int i = 0;

            static void Postfix(Team __instance)
            {
                if (__instance.Banner != null)
                {
                    if (i == 500)
                    {
                        if (__instance.IsAttacker)
                        {
                            FieldInfo _currentTacticField = typeof(TeamAIComponent).GetField("_currentTactic", BindingFlags.NonPublic | BindingFlags.Instance);
                            _currentTacticField.DeclaringType.GetField("_currentTactic");
                            TacticComponent _currentTactic = (TacticComponent)_currentTacticField.GetValue(__instance.TeamAI);

                            InformationManager.DisplayMessage(new InformationMessage("Attacker: " + _currentTactic));
                            foreach (Formation formation in __instance.Formations)
                            {
                                if (formation.QuerySystem.IsMeleeFormation)
                                {
                                    InformationManager.DisplayMessage(new InformationMessage("infantry: " + formation.AI.ActiveBehavior));
                                }
                                else if (formation.QuerySystem.IsCavalryFormation)
                                {
                                    InformationManager.DisplayMessage(new InformationMessage("cavalry: " + formation.AI.ActiveBehavior));
                                }
                                else if (formation.QuerySystem.IsRangedCavalryFormation)
                                {
                                    InformationManager.DisplayMessage(new InformationMessage("ranged cavalry: " + formation.AI.ActiveBehavior));
                                }
                                else if (formation.QuerySystem.IsRangedFormation)
                                {
                                    InformationManager.DisplayMessage(new InformationMessage("ranged: " + formation.AI.ActiveBehavior));
                                }
                            }
                        }
                        else
                        {
                            FieldInfo _currentTacticField = typeof(TeamAIComponent).GetField("_currentTactic", BindingFlags.NonPublic | BindingFlags.Instance);
                            _currentTacticField.DeclaringType.GetField("_currentTactic");
                            TacticComponent _currentTactic = (TacticComponent)_currentTacticField.GetValue(__instance.TeamAI);

                            InformationManager.DisplayMessage(new InformationMessage("Defender: " + _currentTactic));
                            foreach (Formation formation in __instance.Formations)
                            {
                                if (formation.QuerySystem.IsMeleeFormation)
                                {
                                    InformationManager.DisplayMessage(new InformationMessage("infantry: " + formation.AI.ActiveBehavior));
                                }
                                else if (formation.QuerySystem.IsCavalryFormation)
                                {
                                    InformationManager.DisplayMessage(new InformationMessage("cavalry: " + formation.AI.ActiveBehavior));
                                }
                                else if (formation.QuerySystem.IsRangedCavalryFormation)
                                {
                                    InformationManager.DisplayMessage(new InformationMessage("ranged cavalry: " + formation.AI.ActiveBehavior));
                                }
                                else if (formation.QuerySystem.IsRangedFormation)
                                {
                                    InformationManager.DisplayMessage(new InformationMessage("ranged: " + formation.AI.ActiveBehavior));
                                }
                            }
                        }
                        i = 0;
                    }
                    else
                    {
                        i++;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(MissionCombatantsLogic))]
        [HarmonyPatch("EarlyStart")]
        class TeamAiFieldBattle
        {

            static void Postfix()
            {

                if (Mission.Current.Teams.Any())
                {
                    if (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle)
                    {
                        foreach (Team team in Mission.Current.Teams.Where((Team t) => t.HasTeamAi))
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
                                //team.AddTacticOption(new TacticHoldChokePoint(team));
                                //team.AddTacticOption(new TacticHoldTheHill(team));
                                //team.AddTacticOption(new TacticRangedHarrassmentOffensive(team));
                                //team.AddTacticOption(new TacticCoordinatedRetreat(team));
                                team.AddTacticOption(new TacticFullScaleAttack(team));
                                team.AddTacticOption(new TacticFrontalCavalryCharge(team));
                                team.AddTacticOption(new TacticCharge(team));
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
                    ____mainInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(2f);
                }
                if (____archers != null)
                {
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmish>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
                    ____archers.AI.SetBehaviorWeight<BehaviorRegroup>(2f);
                }
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
            static void PostfixAttack(ref Formation ____mainInfantry, ref Formation ____archers, ref Formation ____rightCavalry, ref Formation ____leftCavalry)
            {

                if (____archers != null)
                {
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmish>(1f);
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
                }
                if (____rightCavalry != null)
                {
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                }
                if (____leftCavalry != null)
                {
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                }

                Utilities.FixCharge(ref ____mainInfantry);
            }

            [HarmonyPostfix]
            [HarmonyPatch("HasBattleBeenJoined")]
            static void PostfixHasBattleBeenJoined(Formation ____mainInfantry, bool ____hasBattleBeenJoined, ref bool __result)
            {
                __result = Utilities.HasBattleBeenJoined(____mainInfantry, ____hasBattleBeenJoined, 20f);
            }

            [HarmonyPostfix]
            [HarmonyPatch("GetTacticWeight")]
            static void PostfixGetAiWeight(ref float __result)
            {
                if (Mission.Current.Teams.Any())
                {
                    if (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle)
                    {
                        foreach (Team team in Mission.Current.Teams.Where((Team t) => t.HasTeamAi))
                        {
                            if (team.Side == BattleSideEnum.Defender)
                            {
                                if (team.QuerySystem.InfantryRatio > 0.9f)
                                {
                                    __result = 100f;
                                }
                            }
                        }
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
                    for (int i = 0; i < ____leftCavalry.CountOfUnits; i++)
                    {
                        Agent agent = ____leftCavalry.GetUnitWithIndex(i);
                        bool ismountedSkrimisher = false;
                        for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                        {
                            if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                            {
                                if (agent.Equipment[equipmentIndex].PrimaryItem.Type == ItemTypeEnum.Thrown && agent.MountAgent != null)
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
                    }
                    for (int i = 0; i < ____rightCavalry.CountOfUnits; i++)
                    {
                        Agent agent = ____rightCavalry.GetUnitWithIndex(i);
                        bool ismountedSkrimisher = false;
                        for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                        {
                            if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                            {
                                if (agent.Equipment[equipmentIndex].PrimaryItem.Type == ItemTypeEnum.Thrown && agent.MountAgent != null)
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
                    }

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
                    ____cavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch("Attack")]
            static void PostfixAttack(ref Formation ____mainInfantry, ref Formation ____cavalry)
            {

                if (____cavalry != null)
                {
                    ____cavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                }

                Utilities.FixCharge(ref ____mainInfantry);
            }

            [HarmonyPostfix]
            [HarmonyPatch("HasBattleBeenJoined")]
            static void PostfixHasBattleBeenJoined(Formation ____cavalry, bool ____hasBattleBeenJoined, ref bool __result)
            {
                __result = Utilities.HasBattleBeenJoined(____cavalry, ____hasBattleBeenJoined, 8f);
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
            static void PostfixAttack(ref Formation ____mainInfantry, ref Formation ____leftCavalry, ref Formation ____rightCavalry)
            {

                if (____rightCavalry != null)
                {
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                }
                if (____leftCavalry != null)
                {
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                }

                Utilities.FixCharge(ref ____mainInfantry);
            }

            [HarmonyPostfix]
            [HarmonyPatch("HasBattleBeenJoined")]
            static void PostfixHasBattleBeenJoined(Formation ____mainInfantry, bool ____hasBattleBeenJoined, ref bool __result)
            {
                __result = Utilities.HasBattleBeenJoined(____mainInfantry, ____hasBattleBeenJoined, 20f);
            }

            [HarmonyPostfix]
            [HarmonyPatch("ManageFormationCounts")]
            static void PostfixManageFormationCounts(ref Formation ____leftCavalry, ref Formation ____rightCavalry)
            {
                if (____leftCavalry != null && ____rightCavalry != null)
                {
                    List<Agent> mountedSkirmishersList = new List<Agent>();
                    List<Agent> mountedMeleeList = new List<Agent>();
                    for (int i = 0; i < ____leftCavalry.CountOfUnits; i++)
                    {
                        Agent agent = ____leftCavalry.GetUnitWithIndex(i);
                        bool ismountedSkrimisher = false;
                        for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                        {
                            if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                            {
                                if (agent.Equipment[equipmentIndex].PrimaryItem.Type == ItemTypeEnum.Thrown && agent.MountAgent != null)
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
                    }
                    for (int i = 0; i < ____rightCavalry.CountOfUnits; i++)
                    {
                        Agent agent = ____rightCavalry.GetUnitWithIndex(i);
                        bool ismountedSkrimisher = false;
                        for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                        {
                            if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                            {
                                if (agent.Equipment[equipmentIndex].PrimaryItem.Type == ItemTypeEnum.Thrown && agent.MountAgent != null)
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
                    }

                    int j = 0;
                    int cavalryCount = ____leftCavalry.CountOfUnits + ____rightCavalry.CountOfUnits;
                    foreach (Agent agent in mountedSkirmishersList.ToList())
                    {
                        if (j < cavalryCount / 2)
                        {
                            agent.Formation = ____rightCavalry;
                        }
                        else
                        {
                            agent.Formation = ____leftCavalry;
                        }
                        j++;
                    }
                    foreach (Agent agent in mountedMeleeList.ToList())
                    {
                        if (j < cavalryCount / 2)
                        {
                            agent.Formation = ____rightCavalry;
                        }
                        else
                        {
                            agent.Formation = ____leftCavalry;
                        }
                        j++;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(TacticDefensiveLine))]
        class OverrideTacticDefensiveLine
        {
            [HarmonyPostfix]
            [HarmonyPatch("HasBattleBeenJoined")]
            static void PostfixHasBattleBeenJoined(Formation ____mainInfantry, bool ____hasBattleBeenJoined, ref bool __result)
            {
                __result = Utilities.HasBattleBeenJoined(____mainInfantry, ____hasBattleBeenJoined, 13f);
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
                    ____archers.AI.SetBehaviorWeight<BehaviorRegroup>(2f);
                }
                if (____mainInfantry != null)
                {
                    ____mainInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(2f);
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch("Engage")]
            static void PostfixEngage(ref Formation ____archers, ref Formation ____mainInfantry)
            {
                if (____archers != null)
                {
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmish>(1f);
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
                }

                Utilities.FixCharge(ref ____mainInfantry);
            }

        }

        [HarmonyPatch(typeof(TacticDefensiveEngagement))]
        class OverrideTacticDefensiveEngagement
        {
            [HarmonyPostfix]
            [HarmonyPatch("HasBattleBeenJoined")]
            static void PostfixHasBattleBeenJoined(Formation ____mainInfantry, bool ____hasBattleBeenJoined, ref bool __result)
            {
                __result = Utilities.HasBattleBeenJoined(____mainInfantry, ____hasBattleBeenJoined, 13f);
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
                    ____archers.AI.SetBehaviorWeight<BehaviorRegroup>(2f);
                }
                if (____mainInfantry != null)
                {
                    ____mainInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(2f);
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch("Engage")]
            static void PostfixAttack(ref Formation ____archers, ref Formation ____mainInfantry)
            {
                if (____archers != null)
                {
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmish>(1f);
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
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
                f.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                f.AI.SetBehaviorWeight<BehaviorPullBack>(1f);
                f.AI.SetBehaviorWeight<BehaviorStop>(1f);
                f.AI.SetBehaviorWeight<BehaviorReserve>(0f);

                return false;
            }
        }

        //public class TacticFullScaleAttackMy : TacticComponent
        //{
        //    private bool _hasBattleBeenJoined;

        //    private int _AIControlledFormationCount;

        //    protected Formation meleeInfantry;
        //    protected Formation rangedInfantry;
        //    //protected Formation skirmishersLeft;
        //    //protected Formation skirmishersRight;
        //    protected Formation mountedSkirmishers;
        //    protected Formation cavalryLeft;
        //    protected Formation cavalryRight;
        //    protected Formation cavalryRanged;

        //    protected void ManageFormationCountsMy(int infantryCount, int rangedCount, int cavalryCount, int rangedCavalryCount)
        //    {
        //        SplitFormationClassIntoGivenNumber((Formation f) => f.QuerySystem.IsInfantryFormation, infantryCount);
        //        SplitFormationClassIntoGivenNumber((Formation f) => f.QuerySystem.IsRangedFormation, rangedCount);
        //        SplitFormationClassIntoGivenNumber((Formation f) => f.QuerySystem.IsCavalryFormation, cavalryCount);
        //        SplitFormationClassIntoGivenNumber((Formation f) => f.QuerySystem.IsRangedCavalryFormation, rangedCavalryCount);
        //    }

        //    private void CopyOrdersFrom(Formation source, Formation target)
        //    {
        //        source.MovementOrder = target.MovementOrder;
        //        source.FormOrder = target.FormOrder;
        //        source.SetPositioning(null, null, target.UnitSpacing);
        //        source.RidingOrder = target.RidingOrder;
        //        source.WeaponUsageOrder = target.WeaponUsageOrder;
        //        source.FiringOrder = target.FiringOrder;
        //        source.IsAIControlled = (target.IsAIControlled || !target.Team.IsPlayerGeneral);
        //        source.AI.Side = target.AI.Side;
        //        source.MovementOrder = target.MovementOrder;
        //        source.FacingOrder = target.FacingOrder;
        //        source.ArrangementOrder = target.ArrangementOrder;
        //    }

        //    internal void TransferUnitsAux(Formation source, Formation target)
        //    {
        //        CopyOrdersFrom(target, source);
        //    }

        //    protected void AssignTacticFormations1121()
        //    {
        //        ManageFormationCountsMy(1, 1, 2, 1);
        //        meleeInfantry = ChooseAndSortByPriority(Formations, (Formation f) => f.QuerySystem.IsInfantryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
        //        List<Formation> list = ChooseAndSortByPriority(Formations, (Formation f) => f.QuerySystem.IsCavalryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower);
        //        //Formation formation2 = meleeInfantry.Team.GetFormation((FormationClass)5);
        //        if (meleeInfantry != null)
        //        {
        //            int j = 0;
        //            int testcount = 0;
        //            ArrayList mountedSkirmishersList = new ArrayList();
        //            for(int k = 0; k <list.Count; k++)
        //            {
        //                for (int i = 0; i < list[k].CountOfUnits; i++)
        //                {
        //                    Agent agent = list[k].GetUnitWithIndex(i);

        //                    for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
        //                    {
        //                        if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
        //                        {
        //                            if (agent.Equipment[equipmentIndex].PrimaryItem.Type == ItemTypeEnum.Thrown && agent.MountAgent != null)
        //                            {
        //                                testcount++;
        //                                mountedSkirmishersList.Add(agent);
        //                            }
        //                        }
        //                    }
        //                }
        //            }

        //            j = 0;
        //            foreach (Agent agent in mountedSkirmishersList)
        //            {
        //                agent.Formation = meleeInfantry.Team.GetFormation((FormationClass)5);
        //                j++;
        //            }

        //            mountedSkirmishers = meleeInfantry.Team.GetFormation((FormationClass)5);
        //        }

        //        if (meleeInfantry != null)
        //        {
        //            _mainInfantry = meleeInfantry;
        //            PropertyInfo property = typeof(FormationAI).GetProperty("IsMainFormation", BindingFlags.NonPublic | BindingFlags.Instance);
        //            property.DeclaringType.GetProperty("IsMainFormation");
        //            property.SetValue(meleeInfantry.AI, true, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
        //        }
        //        rangedInfantry = ChooseAndSortByPriority(Formations, (Formation f) => f.QuerySystem.IsRangedFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();

        //        if (list.Count > 0)
        //        {
        //            cavalryLeft = list[0];
        //            cavalryLeft.AI.Side = FormationAI.BehaviorSide.Left;
        //            if (list.Count > 1)
        //            {
        //                cavalryRight = list[1];
        //                cavalryRight.AI.Side = FormationAI.BehaviorSide.Right;
        //            }
        //            else
        //            {
        //                cavalryRight = null;
        //            }
        //        }
        //        else
        //        {
        //            cavalryLeft = null;
        //            cavalryRight = null;
        //        }
        //        cavalryRanged = ChooseAndSortByPriority(Formations, (Formation f) => f.QuerySystem.IsRangedCavalryFormation, (Formation f) => f.IsAIControlled, (Formation f) => f.QuerySystem.FormationPower).FirstOrDefault();
        //    }

        //    protected override void ManageFormationCounts()
        //    {
        //        AssignTacticFormations1121();
        //    }

        //    private void Advance()
        //    {
        //        if (team.IsPlayerTeam && !team.IsPlayerGeneral && team.IsPlayerSergeant)
        //        {
        //            SoundTacticalHorn(TacticComponent.MoveHornSoundIndex);
        //        }

        //        if (meleeInfantry != null && meleeInfantry.CountOfUnits > 0)
        //        {
        //            meleeInfantry.AI.ResetBehaviorWeights();
        //            SetDefaultBehaviorWeights(meleeInfantry);
        //            meleeInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(2f);
        //            meleeInfantry.AI.SetBehaviorWeight<BehaviorAdvance>(1f);
        //        }
        //        if (rangedInfantry != null && rangedInfantry.CountOfUnits > 0)
        //        {
        //            rangedInfantry.AI.ResetBehaviorWeights();
        //            SetDefaultBehaviorWeights(rangedInfantry);
        //            rangedInfantry.AI.SetBehaviorWeight<BehaviorSkirmish>(0f);
        //            rangedInfantry.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
        //            rangedInfantry.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
        //            rangedInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(2f);
        //            }
        //        if (cavalryLeft != null && cavalryLeft.CountOfUnits > 0)
        //        {
        //            cavalryLeft.AI.ResetBehaviorWeights();
        //            SetDefaultBehaviorWeights(cavalryLeft);
        //            cavalryLeft.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = FormationAI.BehaviorSide.Left;
        //            cavalryLeft.AI.SetBehaviorWeight<BehaviorCavalryScreen>(1f);
        //        }
        //        if (cavalryRight != null && cavalryRight.CountOfUnits > 0)
        //        {
        //            cavalryRight.AI.ResetBehaviorWeights();
        //            SetDefaultBehaviorWeights(cavalryRight);
        //            cavalryRight.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = FormationAI.BehaviorSide.Right;
        //            cavalryRight.AI.SetBehaviorWeight<BehaviorCavalryScreen>(1f);
        //        }
        //        if (cavalryRanged != null && cavalryRanged.CountOfUnits > 0)
        //        {
        //            cavalryRanged.AI.ResetBehaviorWeights();
        //            SetDefaultBehaviorWeights(cavalryRanged);
        //            cavalryRanged.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        //            cavalryRanged.AI.SetBehaviorWeight<BehaviorHorseArcherSkirmish>(1f);
        //        }
        //        if (mountedSkirmishers != null && mountedSkirmishers.CountOfUnits > 0)
        //        {
        //            mountedSkirmishers.AI.ResetBehaviorWeights();
        //            SetDefaultBehaviorWeights(mountedSkirmishers);
        //            mountedSkirmishers.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        //            mountedSkirmishers.AI.SetBehaviorWeight<BehaviorHorseArcherSkirmish>(1f);
        //        }
        //    }

        //    private void Attack()
        //    {
        //        if (team.IsPlayerTeam && !team.IsPlayerGeneral && team.IsPlayerSergeant)
        //        {
        //            SoundTacticalHorn(TacticComponent.AttackHornSoundIndex);
        //        }
        //        if (meleeInfantry != null && meleeInfantry.CountOfUnits > 0)
        //        {
        //            Utilities.FixCharge(ref meleeInfantry);
        //        }
        //        if (rangedInfantry != null && rangedInfantry.CountOfUnits > 0)
        //        {
        //            rangedInfantry.AI.ResetBehaviorWeights();
        //            SetDefaultBehaviorWeights(rangedInfantry);
        //            rangedInfantry.AI.SetBehaviorWeight<BehaviorSkirmish>(1f);
        //            rangedInfantry.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
        //            rangedInfantry.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
        //        }
        //        if (cavalryLeft != null && cavalryLeft.CountOfUnits > 0)
        //        {
        //            cavalryLeft.AI.ResetBehaviorWeights();
        //            SetDefaultBehaviorWeights(cavalryLeft);
        //            cavalryLeft.AI.SetBehaviorWeight<BehaviorFlank>(1f);
        //            cavalryLeft.AI.SetBehaviorWeight<BehaviorTacticalCharge>(1f);
        //        }
        //        if (cavalryRight != null && cavalryRight.CountOfUnits > 0)
        //        {
        //            cavalryRight.AI.ResetBehaviorWeights();
        //            SetDefaultBehaviorWeights(cavalryRight);
        //            cavalryRight.AI.SetBehaviorWeight<BehaviorFlank>(1f);
        //            cavalryRight.AI.SetBehaviorWeight<BehaviorTacticalCharge>(1f);
        //        }
        //        if (cavalryRanged != null && cavalryRanged.CountOfUnits > 0)
        //        {
        //            cavalryRanged.AI.ResetBehaviorWeights();
        //            SetDefaultBehaviorWeights(cavalryRanged);
        //            cavalryRanged.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        //            cavalryRanged.AI.SetBehaviorWeight<BehaviorHorseArcherSkirmish>(1f);
        //        }
        //        if (mountedSkirmishers != null && mountedSkirmishers.CountOfUnits > 0)
        //        {
        //            mountedSkirmishers.AI.ResetBehaviorWeights();
        //            SetDefaultBehaviorWeights(mountedSkirmishers);
        //            mountedSkirmishers.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
        //            mountedSkirmishers.AI.SetBehaviorWeight<BehaviorHorseArcherSkirmish>(1f);
        //        }
        //    }

        //    public TacticFullScaleAttackMy(Team team)
        //        : base(team)
        //    {
        //        _hasBattleBeenJoined = false;
        //        _AIControlledFormationCount = base.Formations.Count((Formation f) => f.IsAIControlled);
        //    }

        //    private bool HasBattleBeenJoined()
        //    {
        //        return  Utilities.HasBattleBeenJoined(meleeInfantry, _hasBattleBeenJoined, 20f);
        //    }

        //    protected override bool CheckAndSetAvailableFormationsChanged()
        //    {
        //        int num = base.Formations.Count((Formation f) => f.IsAIControlled);
        //        bool num2 = num != _AIControlledFormationCount;
        //        if (num2)
        //        {
        //            _AIControlledFormationCount = num;
        //        }
        //        if (!num2)
        //        {
        //            if ((meleeInfantry == null || (meleeInfantry.CountOfUnits != 0 && meleeInfantry.QuerySystem.IsInfantryFormation)) && (rangedInfantry == null || (rangedInfantry.CountOfUnits != 0 && rangedInfantry.QuerySystem.IsRangedFormation)) && (cavalryLeft == null || (cavalryLeft.CountOfUnits != 0 && cavalryLeft.QuerySystem.IsCavalryFormation)) && (cavalryRight == null || (cavalryRight.CountOfUnits != 0 && cavalryRight.QuerySystem.IsCavalryFormation)))
        //            {
        //                if (cavalryRanged != null)
        //                {
        //                    if (cavalryRanged.CountOfUnits != 0)
        //                    {
        //                        return !cavalryRanged.QuerySystem.IsRangedCavalryFormation;
        //                    }
        //                    return true;
        //                }
        //                return false;
        //            }
        //            return true;
        //        }
        //        return true;
        //    }

        //    protected override void TickOccasionally()
        //    {
        //        if (!base.AreFormationsCreated)
        //        {
        //            return;
        //        }
        //        if (CheckAndSetAvailableFormationsChanged())
        //        {
        //            ManageFormationCounts();
        //            if (_hasBattleBeenJoined)
        //            {
        //                Attack();
        //            }
        //            else
        //            {
        //                Advance();
        //            }
        //        }
        //        bool flag = HasBattleBeenJoined();
        //        if (flag != _hasBattleBeenJoined || IsTacticReapplyNeeded)
        //        {
        //            _hasBattleBeenJoined = flag;
        //            if (_hasBattleBeenJoined)
        //            {
        //                Attack();
        //            }
        //            else
        //            {
        //                Advance();
        //            }
        //            IsTacticReapplyNeeded = false;
        //        }
        //        base.TickOccasionally();
        //    }

        //    internal float GetTacticWeight()
        //    {
        //        float num = team.QuerySystem.RangedCavalryRatio * (float)team.QuerySystem.MemberCount;
        //        return team.QuerySystem.InfantryRatio * (float)team.QuerySystem.MemberCount / ((float)team.QuerySystem.MemberCount - num) * (float)System.Math.Sqrt(team.QuerySystem.OverallPowerRatio);
        //    }

        //    internal static void SetDefaultBehaviorWeights(Formation f)
        //    {
        //        f.AI.SetBehaviorWeight<BehaviorCharge>(1f);
        //        f.AI.SetBehaviorWeight<BehaviorPullBack>(1f);
        //        f.AI.SetBehaviorWeight<BehaviorStop>(1f);
        //        f.AI.SetBehaviorWeight<BehaviorReserve>(1f);
        //    }
        //}


    }
}