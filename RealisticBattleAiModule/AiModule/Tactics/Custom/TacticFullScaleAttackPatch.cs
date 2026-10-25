using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace RBMAI.AiModule.RbmTactics
{
    [HarmonyPatch(typeof(TacticFullScaleAttack))]
    internal class TacticFullScaleAttackPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch("Advance")]
        private static void PostfixAdvance(TacticFullScaleAttack __instance, ref Formation ____mainInfantry, ref Formation ____archers, ref Formation ____rightCavalry, ref Formation ____leftCavalry, ref Formation ____rangedCavalry)
        {
            if (____mainInfantry != null)
            {
                ____mainInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(1.75f);
                // Native leaves BehaviorCharge at 1, overtaking BehaviorAdvance late in the approach; PostfixAttack re-arms it.
                ____mainInfantry.AI.SetBehaviorWeight<BehaviorCharge>(0f);
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
                // Off a flank TerrainFlankPlan covers.
                FormationAI.BehaviorSide rightSide = TerrainFlankPlan.CavalrySide(__instance, ____rightCavalry, FormationAI.BehaviorSide.Right, false);
                ____rightCavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = rightSide;
                ____rightCavalry.AI.SetBehaviorWeight<RBMBehaviorForwardSkirmish>(1f).FlankSide = rightSide;
            }
            if (____leftCavalry != null)
            {
                ____leftCavalry.AI.ResetBehaviorWeights();
                FormationAI.BehaviorSide leftSide = TerrainFlankPlan.CavalrySide(__instance, ____leftCavalry, FormationAI.BehaviorSide.Left, ____rightCavalry == null);
                ____leftCavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = leftSide;
                ____leftCavalry.AI.SetBehaviorWeight<RBMBehaviorForwardSkirmish>(1f).FlankSide = leftSide;
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
        [HarmonyPatch("Attack")]
        private static void PostfixAttack(ref Formation ____mainInfantry, ref Formation ____archers, ref Formation ____rightCavalry, ref Formation ____leftCavalry, ref Formation ____rangedCavalry)
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
                TacticFullScaleAttack.SetDefaultBehaviorWeights(____rangedCavalry);
                ____rangedCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
            }
            RBMAI.Utilities.FixCharge(ref ____mainInfantry);
        }

        [HarmonyPostfix]
        [HarmonyPatch("HasBattleBeenJoined")]
        private static void PostfixHasBattleBeenJoined(TacticFullScaleAttack __instance, Formation ____mainInfantry, bool ____hasBattleBeenJoined, ref bool __result)
        {
            // Latched per team so kiting enemies do not flip Attack / Advance every tick.
            __result = RBMAI.Utilities.HasAttackBeenJoined(____mainInfantry, __instance.Team, ____hasBattleBeenJoined);
        }

        [HarmonyPostfix]
        [HarmonyPatch("GetTacticWeight")]
        private static void PostfixGetAiWeight(TacticFullScaleAttack __instance, ref float __result)
        {
            Team currentTeam = __instance.Team;
            if (currentTeam.Side == BattleSideEnum.Defender)
            {
                if (currentTeam.QuerySystem.InfantryRatio > 0.9f)
                {
                    __result = 100f;
                }
            }
            if (float.IsNaN(__result))
            {
                __result = 0.01f;
            }
            // All-mounted army: every option weighs ~0 (native uses the infantry share), so ride out as a full-scale attack unless the wedge weighs more.
            TeamQuerySystem q = currentTeam.QuerySystem;
            if (q != null && q.MemberCount > 0 && q.CavalryRatio + q.RangedCavalryRatio >= MountedArmyShare)
            {
                __result = TaleWorlds.Library.MathF.Max(__result,
                    MountedArmyWeight * TaleWorlds.Library.MathF.Sqrt(TaleWorlds.Library.MathF.Max(q.RemainingPowerRatio, 0.05f)));
            }
        }

        private const float MountedArmyShare = 0.9f; // cavalry + horse archers
        private const float MountedArmyWeight = 0.5f;

        /// <summary>Per tactic instance: last re-deal time; whether the current ManageFormationCounts call is refresh-only.</summary>
        private sealed class DealState
        {
            public float LastDealAt = float.NaN;
            public bool RefreshOnly;
        }

        private static readonly ConditionalWeakTable<TacticFullScaleAttack, DealState> _dealStates = new ConditionalWeakTable<TacticFullScaleAttack, DealState>();

        private const float DealRefreshInterval = 30f;

        /// <summary>Once the battle is joined, or within DealRefreshInterval of the last deal, a formation-count change only refreshes references (RBM's ManageFormationCounts prefix would pull every AI man back and re-deal the riders).</summary>
        [HarmonyPrefix]
        [HarmonyPatch("ManageFormationCounts")]
        private static bool PrefixManageFormationCounts(TacticFullScaleAttack __instance, bool ____hasBattleBeenJoined, ref Formation ____mainInfantry, ref Formation ____archers,
            ref Formation ____leftCavalry, ref Formation ____rightCavalry, ref Formation ____rangedCavalry)
        {
            DealState state = _dealStates.GetOrCreateValue(__instance);
            state.RefreshOnly = false;
            Mission mission = Mission.Current;
            Team team = __instance.Team;
            if (mission == null || team == null || !mission.IsFieldBattle)
            {
                return true;
            }
            float now = mission.CurrentTime;
            string why = ____hasBattleBeenJoined ? "battleJoined"
                : (!float.IsNaN(state.LastDealAt) && now - state.LastDealAt < DealRefreshInterval) ? "interval " + (int)(now - state.LastDealAt) + "s"
                : null;
            if (why == null)
            {
                state.LastDealAt = now;
                return true;
            }
            state.RefreshOnly = true;
            List<Formation> formations = team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList();
            ____mainInfantry = Strongest(formations, (Formation f) => f.QuerySystem.IsInfantryFormation, null, null);
            ____archers = Strongest(formations, (Formation f) => f.QuerySystem.IsRangedFormation, ____mainInfantry, null);
            ____leftCavalry = Strongest(formations, (Formation f) => f.QuerySystem.IsCavalryFormation, null, null);
            ____rightCavalry = Strongest(formations, (Formation f) => f.QuerySystem.IsCavalryFormation, ____leftCavalry, null);
            ____rangedCavalry = Strongest(formations, (Formation f) => f.QuerySystem.IsRangedCavalryFormation, ____leftCavalry, ____rightCavalry);
            if (____mainInfantry != null)
            {
                ____mainInfantry.AI.IsMainFormation = true;
                ____mainInfantry.AI.Side = FormationAI.BehaviorSide.Middle;
            }
            if (____leftCavalry != null)
            {
                ____leftCavalry.AI.Side = FormationAI.BehaviorSide.Left;
            }
            if (____rightCavalry != null)
            {
                ____rightCavalry.AI.Side = FormationAI.BehaviorSide.Right;
            }
            RBMAI.Tactics.MergeStrayFormations(team, ____mainInfantry, ____archers, ____leftCavalry, ____rangedCavalry, ____rightCavalry);
            if (____mainInfantry != null)
            {
                TerrainSpots.EnqueueBehavior(team, ____mainInfantry, now, "FullScaleAttack", "refreshOnly", "formations", why + " (no deal)");
            }
            return false;
        }

        /// <summary>The strongest formation meeting <paramref name="test"/>, AI-controlled ones first, other than the two excluded.</summary>
        private static Formation Strongest(List<Formation> formations, System.Func<Formation, bool> test, Formation exclude1, Formation exclude2)
        {
            Formation best = null;
            foreach (Formation f in formations)
            {
                if (f == exclude1 || f == exclude2 || !test(f))
                {
                    continue;
                }
                if (best == null || (f.IsAIControlled && !best.IsAIControlled)
                    || (f.IsAIControlled == best.IsAIControlled && f.QuerySystem.FormationPower > best.QuerySystem.FormationPower))
                {
                    best = f;
                }
            }
            return best;
        }

        [HarmonyPostfix]
        [HarmonyPatch("ManageFormationCounts")]
        private static void PostfixManageFormationCounts(TacticFullScaleAttack __instance, ref Formation ____leftCavalry, ref Formation ____rightCavalry)
        {
            DealState state;
            if (_dealStates.TryGetValue(__instance, out state) && state.RefreshOnly)
            {
                // References-only refresh: the riders stay where they are.
                return;
            }
            if (____leftCavalry != null && ____rightCavalry != null && ____leftCavalry.IsAIControlled && ____rightCavalry.IsAIControlled)
            {
                List<Agent> mountedSkirmishersList = new List<Agent>();
                List<Agent> mountedMeleeList = new List<Agent>();
                ____leftCavalry.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                {
                    RBMAI.Tactics.ClassifyMountedAgent(agent, mountedSkirmishersList, mountedMeleeList);
                });
                ____rightCavalry.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                {
                    RBMAI.Tactics.ClassifyMountedAgent(agent, mountedSkirmishersList, mountedMeleeList);
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