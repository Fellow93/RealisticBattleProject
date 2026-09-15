using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI.AiModule.RbmTactics
{
    [HarmonyPatch(typeof(TacticDefensiveLine))]
    internal class TacticDefensiveLinePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch("IsTacticalPositionEligible")]
        private static void PostfixIsTacticalPositionEligible(TacticalPosition tacticalPosition, ref bool __result)
        {
            if (__result && Mission.Current != null)
            {
                Vec2 posVec2 = tacticalPosition.Position.AsVec2;
                float distFromBoundary = Mission.Current.GetClosestBoundaryPosition(posVec2).Distance(posVec2);
                if (distFromBoundary <= 100f)
                {
                    __result = false;
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("HasBattleBeenJoined")]
        private static bool PrefixHasBattleBeenJoined(ref Formation ____mainInfantry, ref bool ____hasBattleBeenJoined, ref bool __result)
        {
            __result = RBMAI.Utilities.HasBattleBeenJoined(____mainInfantry, ____hasBattleBeenJoined);
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch("Defend")]
        private static void PostfixDefend(ref Formation ____archers, ref Formation ____mainInfantry, ref Formation ____rightCavalry, ref Formation ____leftCavalry, ref Formation ____rangedCavalry)
        {
            if (____archers != null)
            {
                ____archers.AI.SetBehaviorWeight<BehaviorSkirmish>(0f);
                ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
                ____archers.AI.SetBehaviorWeight<BehaviorRegroup>(1.25f);
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
            if (____rangedCavalry != null)
            {
                ____rangedCavalry.AI.ResetBehaviorWeights();
                TacticFullScaleAttack.SetDefaultBehaviorWeights(____rangedCavalry);
                ____rangedCavalry.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
                ____rangedCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("Engage")]
        private static void PostfixEngage(ref Formation ____archers, ref Formation ____mainInfantry, ref Formation ____rightCavalry, ref Formation ____leftCavalry, ref Formation ____rangedCavalry)
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
                //____rightCavalry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                ____rightCavalry.AI.SetBehaviorWeight<RBMBehaviorCavalryCharge>(1f);
            }
            if (____leftCavalry != null)
            {
                ____leftCavalry.AI.ResetBehaviorWeights();
                ____leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                //____leftCavalry.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                ____leftCavalry.AI.SetBehaviorWeight<RBMBehaviorCavalryCharge>(1f);
            }
            if (____rangedCavalry != null)
            {
                ____rangedCavalry.AI.ResetBehaviorWeights();
                //TacticFullScaleAttack.SetDefaultBehaviorWeights(____rangedCavalry);
                ____rangedCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
            }
            RBMAI.Utilities.FixCharge(ref ____mainInfantry);
        }
    }
}