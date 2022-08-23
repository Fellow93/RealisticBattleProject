using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ItemObject;

namespace RBMAI.AiModule.RbmTactics
{
    [HarmonyPatch(typeof(TacticFullScaleAttack))]
    class TacticFullScaleAttackPatch
    {

        [HarmonyPostfix]
        [HarmonyPatch("Advance")]
        static void PostfixAdvance(ref Formation ____mainInfantry, ref Formation ____archers, ref Formation ____rightCavalry, ref Formation ____leftCavalry, ref Formation ____rangedCavalry)
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
                ____archers.AI.SetBehaviorWeight<BehaviorRegroup>(1.25f);
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
            if(____rangedCavalry != null)
            {
                ____rangedCavalry.AI.ResetBehaviorWeights();
                TacticFullScaleAttack.SetDefaultBehaviorWeights(____rangedCavalry);
                ____rangedCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("Attack")]
        static void PostfixAttack(ref Formation ____mainInfantry, ref Formation ____archers, ref Formation ____rightCavalry, ref Formation ____leftCavalry, ref Formation ____rangedCavalry)
        {

            if (____archers != null)
            {
                ____archers.AI.ResetBehaviorWeights();
                ____archers.AI.AddAiBehavior(new RBMBehaviorArcherSkirmish(____archers));
                ____archers.AI.SetBehaviorWeight<RBMBehaviorArcherSkirmish>(1f);
                ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(0f);
            }
            if (____rightCavalry != null)
            {
                ____rightCavalry.AI.ResetBehaviorWeights();
                ____rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                ____rightCavalry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                ____rightCavalry.AI.SetBehaviorWeight<RBMBehaviorCavalryCharge>(1f);
            }
            if (____leftCavalry != null)
            {
                ____leftCavalry.AI.ResetBehaviorWeights();
                ____leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                ____leftCavalry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                ____leftCavalry.AI.SetBehaviorWeight<RBMBehaviorCavalryCharge>(1f);
            }
            if (____rangedCavalry != null)
            {
                ____rangedCavalry.AI.ResetBehaviorWeights();
                TacticFullScaleAttack.SetDefaultBehaviorWeights(____rangedCavalry);
                ____rangedCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
            }
            RBMAI.Utilities.FixCharge(ref ____mainInfantry);
        }

        [HarmonyPostfix]
        [HarmonyPatch("HasBattleBeenJoined")]
        static void PostfixHasBattleBeenJoined(Formation ____mainInfantry, bool ____hasBattleBeenJoined, ref bool __result)
        {
            __result = RBMAI.Utilities.HasBattleBeenJoined(____mainInfantry, ____hasBattleBeenJoined);
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
}
