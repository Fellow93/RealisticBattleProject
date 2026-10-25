using HarmonyLib;
using System.Collections.Concurrent;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    public static partial class Tactics
    {
        /// <summary>Minimum time a field-battle team keeps a tactic before it may pick another. Native re-decides every 5 s
        /// with no dwell, and its x1.5 bonus for the current tactic is skipped by the final check for every tactic whose
        /// ResetTacticalPositions returns false (all but DefensiveLine/Ring/HoldChokePoint), so a weight that jumps
        /// switches at once. The 2026-10-20 22:02 log: the attacker flipped Embolon (weight 5 / 0.01 as its heavy-cavalry
        /// share crossed 60%) and FullScaleAttack every 5-15 s, re-splitting its cavalry each time; the defender flipped
        /// DefensiveEngagement and FullScaleAttack with both weights near 0.
        /// While the dwell holds, MakeDecision is skipped except for IsDefenseApplicable (the defender tactics' weights
        /// read it); a team with no enemy left always runs native, which forces TacticCharge.</summary>
        [HarmonyPatch(typeof(TeamAIComponent), "MakeDecision")]
        internal static class TacticDwell
        {
            private const float MinTacticDwellSeconds = 30f;

            /// <summary>Mission time of each team's last tactic change. Written on the AI tick and from ResetTactic (which
            /// may run on the main thread), hence concurrent. Cleared in MissionStartReset. BattleTerrainLogic removes every
            /// team-AI team's entry once, at the terrain publish (terrainTacticsEnabled), so the pick made before the
            /// ground was read is re-decided with the terrain factor at the next decision.</summary>
            internal static readonly ConcurrentDictionary<Team, float> tacticSince = new ConcurrentDictionary<Team, float>();

            [HarmonyPrefix]
            private static bool Prefix(TeamAIComponent __instance, Team ___Team, TacticComponent ____currentTactic, out TacticComponent __state)
            {
                __state = ____currentTactic;
                if (____currentTactic == null || ___Team == null || !(__instance is TeamAIGeneral))
                {
                    return true;
                }
                if (!tacticSince.TryGetValue(___Team, out float since) || Mission.Current.CurrentTime - since >= MinTacticDwellSeconds)
                {
                    return true;
                }
                if (!HasEnemyLeft(___Team))
                {
                    return true;
                }
                __instance.CheckIsDefenseApplicable();
                return false;
            }

            [HarmonyPostfix]
            private static void Postfix(Team ___Team, TacticComponent ____currentTactic, TacticComponent __state)
            {
                if (___Team != null && ____currentTactic != __state)
                {
                    tacticSince[___Team] = Mission.Current.CurrentTime;
                }
            }

            private static bool HasEnemyLeft(Team team)
            {
                foreach (Team other in Mission.Current.Teams)
                {
                    if (other.IsEnemyOf(team) && other.HasAnyFormationsIncludingSpecialThatIsNotEmpty())
                    {
                        return true;
                    }
                }
                return false;
            }
        }
    }
}
