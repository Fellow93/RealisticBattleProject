using HarmonyLib;
using SandBox.Missions.MissionLogics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.GauntletUI.Mission.Singleplayer;
using TaleWorlds.MountAndBlade.ViewModelCollection.HUD.FormationMarker;
using static TaleWorlds.Core.ItemObject;

namespace RBMAI
{
    public static partial class Tactics
    {
        [HarmonyPatch(typeof(TeamAIGeneral))]
        private class OverrideTeamAIGeneral
        {
            [HarmonyPostfix]
            [HarmonyPatch("OnUnitAddedToFormationForTheFirstTime")]
            private static void PostfixOnUnitAddedToFormationForTheFirstTime(Formation formation)
            {
                formation.QuerySystem.Expire();
                // Fires on every 0 -> >0 unit transition and behaviors are never removed: guard against duplicates.
                if (formation.AI.GetBehavior<RBMBehaviorArcherSkirmish>() != null)
                {
                    return;
                }
                formation.AI.AddAiBehavior(new RBMBehaviorArcherSkirmish(formation));
                formation.AI.AddAiBehavior(new RBMBehaviorForwardSkirmish(formation));
                formation.AI.AddAiBehavior(new RBMBehaviorInfantryAttackFlank(formation));
                formation.AI.AddAiBehavior(new RBMBehaviorCavalryCharge(formation));
                formation.AI.AddAiBehavior(new RBMBehaviorEmbolon(formation));
                formation.AI.AddAiBehavior(new RBMBehaviorArcherFlank(formation));
                formation.AI.AddAiBehavior(new RBMBehaviorHorseArcherSkirmish(formation));
                formation.AI.AddAiBehavior(new RBMBehaviorHoldChoke(formation));
                formation.AI.AddAiBehavior(new RBMBehaviorHoldPosition(formation));
                formation.AI.AddAiBehavior(new RBMBehaviorReservePlug(formation));
                formation.AI.AddAiBehavior(new RBMBehaviorHedgehog(formation));
                // Dormant (WeightFactor 0) until the team's ReactionMonitor gives the formation a task.
                formation.AI.AddAiBehavior(new RBMBehaviorReact(formation));            }
        }

        //private static float originalDefenderPower = 0f;
        //private static float originalAttackerPower = 0f;

        // Every tactic is offered to every culture; TacticSelection applies culture x commander x jitter. The all-cavalry-remnant rout lives in TacticSelection.
        [HarmonyPatch(typeof(MissionCombatantsLogic))]
        [HarmonyPatch("EarlyStart")]
        public class EarlyStartPatch
        {
            public static void Postfix(ref IBattleCombatant ___AttackerLeaderBattleCombatant, ref IBattleCombatant ___DefenderLeaderBattleCombatant)
            {
                if (Mission.Current.Teams.Any())
                {
                    if (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle)
                    {
                        foreach (Team team in Mission.Current.Teams.Where((Team t) => t.HasTeamAi).ToList())
                        {
                            if (team.Side == BattleSideEnum.Attacker)
                            {
                                IBattleCombatant leader = ___AttackerLeaderBattleCombatant;
                                team.ClearTacticOptions();
                                AddTacticOption(team, new RBMTacticEmbolon(team), leader);
                                AddTacticOption(team, new RBMTacticAttackSplitSkirmishers(team), leader);
                                AddTacticOption(team, new RBMTacticAttackSplitInfantry(team), leader);
                                AddTacticOption(team, new RBMTacticAttackSplitArchers(team), leader);
                                AddTacticOption(team, new TacticRangedHarrassmentOffensive(team), leader);
                                AddTacticOption(team, new TacticFullScaleAttack(team), leader);
                            }
                            if (team.Side == BattleSideEnum.Defender)
                            {
                                IBattleCombatant leader = ___DefenderLeaderBattleCombatant;
                                team.ClearTacticOptions();
                                AddTacticOption(team, new RBMTacticDefendSplitArchers(team), leader);
                                AddTacticOption(team, new TacticDefensiveEngagement(team), leader);
                                AddTacticOption(team, new TacticDefensiveLine(team), leader);
                                AddTacticOption(team, new RBMTacticDefendSplitInfantry(team), leader);
                                AddTacticOption(team, new RBMTacticHoldChoke(team), leader);
                                AddTacticOption(team, new RBMTacticReservePlug(team), leader);
                                AddTacticOption(team, new RBMTacticHedgehog(team), leader);
                                AddTacticOption(team, new TacticFullScaleAttack(team), leader);
                            }
                        }
                    }
                }
            }

            private static void AddTacticOption(Team team, TacticComponent tactic, IBattleCombatant leader)
            {
                // A bandit leader party switches the side's terrain plans off (ArmyScale).
                ArmyScale.NoteLeader(team.Side, leader);
                team.AddTacticOption(tactic);
                TacticSelection.Register(team, tactic, leader);
            }
        }
    }
}
