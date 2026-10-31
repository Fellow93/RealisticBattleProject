using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    [HarmonyPatch(typeof(TacticDefensiveEngagement))]
    internal class TacticDefensiveEngagementPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("HasBattleBeenJoined")]
        private static bool PrefixHasBattleBeenJoined(TacticDefensiveEngagement __instance, Formation ____mainInfantry, bool ____hasBattleBeenJoined, ref bool __result)
        {
            // While the battle position's line (RBMBehaviorHoldPosition) holds, only its own latched engage.
            __result = RBMBehaviorHoldPosition.BattleJoinedFor(____mainInfantry, __instance, ____hasBattleBeenJoined);
            return false;
        }

        /// <summary>Re-applies Defend when the battle position appears or moves.</summary>
        [HarmonyPrefix]
        [HarmonyPatch("TickOccasionally")]
        private static void PrefixTickOccasionally(TacticDefensiveEngagement __instance, ref bool ___IsTacticReapplyNeeded)
        {
            if (TerrainBattlePosition.NeedsReapply(__instance))
            {
                ___IsTacticReapplyNeeded = true;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("Defend")]
        private static void PostfixDefend(TacticDefensiveEngagement __instance, ref Formation ____archers, ref Formation ____mainInfantry, ref Formation ____rightCavalry, ref Formation ____leftCavalry, ref Formation ____rangedCavalry)
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
                // Off a flank TerrainFlankPlan covers.
                ____rightCavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = TerrainFlankPlan.CavalrySide(__instance, ____rightCavalry, FormationAI.BehaviorSide.Right, false);
            }
            if (____leftCavalry != null)
            {
                ____leftCavalry.AI.ResetBehaviorWeights();
                ____leftCavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = TerrainFlankPlan.CavalrySide(__instance, ____leftCavalry, FormationAI.BehaviorSide.Left, ____rightCavalry == null);
            }
            if (____rangedCavalry != null)
            {
                ____rangedCavalry.AI.ResetBehaviorWeights();
                TacticFullScaleAttack.SetDefaultBehaviorWeights(____rangedCavalry);
                ____rangedCavalry.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
                ____rangedCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
            }
            // The defender's battle position (needs a published plan): infantry holds the line (no melee-infantry main: the archer anchor stands), archers their place, cavalry reserves when both ends are covered.
            bool archerLine = RBMBehaviorHoldPosition.YieldToArcherStand(__instance.Team, __instance, 0, ____mainInfantry, ____archers);
            if (archerLine || RBMBehaviorHoldPosition.TryAssign(____mainInfantry, __instance, 0, RBMBehaviorHoldPosition.Role.Line))
            {
                if (!archerLine)
                {
                    RBMBehaviorHoldPosition.TryAssign(____archers, __instance, 0, RBMBehaviorHoldPosition.Role.Archers);
                }
                if (TerrainBattlePosition.ReserveWanted(__instance.Team))
                {
                    RBMBehaviorHoldPosition.TryAssign(____leftCavalry, __instance, 0, RBMBehaviorHoldPosition.Role.Reserve);
                    RBMBehaviorHoldPosition.TryAssign(____rightCavalry, __instance, 0, RBMBehaviorHoldPosition.Role.Reserve);
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("Engage")]
        private static void PostfixAttack(ref Formation ____archers, ref Formation ____mainInfantry, ref Formation ____rightCavalry, ref Formation ____leftCavalry, ref Formation ____rangedCavalry)
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
                //TacticComponent.SetDefaultBehaviorWeights(____rightCavalry);
                ____rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                ____rightCavalry.AI.SetBehaviorWeight<RBMBehaviorCavalryCharge>(1f);
            }
            if (____leftCavalry != null)
            {
                ____leftCavalry.AI.ResetBehaviorWeights();
                //TacticComponent.SetDefaultBehaviorWeights(____leftCavalry);
                ____leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
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