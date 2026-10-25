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
        private static bool PrefixHasBattleBeenJoined(TacticDefensiveLine __instance, ref Formation ____mainInfantry, ref bool ____hasBattleBeenJoined, ref bool __result)
        {
            // While the battle position's line (RBMBehaviorHoldPosition) holds, only its own latched engage.
            __result = RBMBehaviorHoldPosition.BattleJoinedFor(____mainInfantry, __instance, ____hasBattleBeenJoined);
            return false;
        }

        /// <summary>Re-applies Defend when the battle position appears or moves.</summary>
        [HarmonyPrefix]
        [HarmonyPatch("TickOccasionally")]
        private static void PrefixTickOccasionally(TacticDefensiveLine __instance, ref bool ___IsTacticReapplyNeeded)
        {
            if (TerrainBattlePosition.NeedsReapply(__instance))
            {
                ___IsTacticReapplyNeeded = true;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("Defend")]
        private static void PostfixDefend(TacticDefensiveLine __instance, ref Formation ____archers, ref Formation ____mainInfantry, ref Formation ____rightCavalry, ref Formation ____leftCavalry, ref Formation ____rangedCavalry)
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
            // The defender's battle position, unless it IS the current line (then native's BehaviorDefend holds): infantry holds the line, archers their place, cavalry reserves when both ends are covered.
            TerrainBattlePosition.Plan plan = TerrainBattlePosition.PlanFor(__instance.Team);
            if (plan != null && !plan.IsStay && RBMBehaviorHoldPosition.TryAssign(____mainInfantry, __instance, 0, RBMBehaviorHoldPosition.Role.Line))
            {
                RBMBehaviorHoldPosition.TryAssign(____archers, __instance, 0, RBMBehaviorHoldPosition.Role.Archers);
                if (TerrainBattlePosition.ReserveWanted(__instance.Team))
                {
                    RBMBehaviorHoldPosition.TryAssign(____leftCavalry, __instance, 0, RBMBehaviorHoldPosition.Role.Reserve);
                    RBMBehaviorHoldPosition.TryAssign(____rightCavalry, __instance, 0, RBMBehaviorHoldPosition.Role.Reserve);
                }
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