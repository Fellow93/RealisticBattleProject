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
                formation.AI.AddAiBehavior(new RBMBehaviorArcherSkirmish(formation));
                formation.AI.AddAiBehavior(new RBMBehaviorForwardSkirmish(formation));
                formation.AI.AddAiBehavior(new RBMBehaviorInfantryAttackFlank(formation));
                formation.AI.AddAiBehavior(new RBMBehaviorCavalryCharge(formation));
                formation.AI.AddAiBehavior(new RBMBehaviorEmbolon(formation));
                formation.AI.AddAiBehavior(new RBMBehaviorArcherFlank(formation));
                formation.AI.AddAiBehavior(new RBMBehaviorHorseArcherSkirmish(formation));
            }
        }

        [HarmonyPatch(typeof(CampaignMissionComponent))]
        [HarmonyPatch("EarlyStart")]
        public class CampaignMissionComponentPatch
        {
            public static void Postfix()
            {
                AgentStances.values.Clear();
                StanceLogic.agentsToDropShield.Clear();
                StanceLogic.agentsToDropWeapon.Clear();
                StanceLogic.agentsToChangeFormation.Clear();
                //RBMAiPatcher.DoPatching();
                agentDamage.Clear();
            }
        }

        //private static float originalDefenderPower = 0f;
        //private static float originalAttackerPower = 0f;

        [HarmonyPatch(typeof(MissionCombatantsLogic))]
        [HarmonyPatch("EarlyStart")]
        public class EarlyStartPatch
        {
            public static void Postfix(ref IBattleCombatant ___AttackerLeaderBattleCombatant, ref IBattleCombatant ___DefenderLeaderBattleCombatant)
            {
                Frontline.aiDecisionCooldownDict.Clear();
                agentDamage.Clear();
                //RBMAiPatcher.DoPatching();
                AgentAi.OnTickPatch.itemPickupDistanceStorage.Clear();
                StanceLogic.agentsToChangeFormation.Clear();
                StanceLogic.agentsToDropWeapon.Clear();
                StanceLogic.agentsToDropShield.Clear();
                AgentStances.values.Clear();
                //originalDefenderPower = 0f;
                //originalAttackerPower = 0f;
                if (Mission.Current.Teams.Any())
                {
                    if (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle)
                    {
                        foreach (Team team in Mission.Current.Teams.Where((Team t) => t.HasTeamAi).ToList())
                        {
                            if (team.Side == BattleSideEnum.Attacker)
                            {
                                team.ClearTacticOptions();
                                if (___AttackerLeaderBattleCombatant?.BasicCulture?.StringId == "empire")
                                {
                                    team.AddTacticOption(new RBMTacticEmbolon(team));
                                }
                                else
                                {
                                    //team.AddTacticOption(new TacticFrontalCavalryCharge(team));
                                }
                                if (___AttackerLeaderBattleCombatant?.BasicCulture?.StringId == "aserai" || ___AttackerLeaderBattleCombatant?.BasicCulture?.StringId == "darshi")
                                {
                                    team.AddTacticOption(new RBMTacticAttackSplitSkirmishers(team));
                                }
                                if (___AttackerLeaderBattleCombatant?.BasicCulture?.StringId == "sturgia" || ___AttackerLeaderBattleCombatant?.BasicCulture?.StringId == "nord")
                                {
                                    team.AddTacticOption(new RBMTacticAttackSplitInfantry(team));
                                }
                                if (___AttackerLeaderBattleCombatant?.BasicCulture?.StringId == "battania")
                                {
                                    team.AddTacticOption(new RBMTacticAttackSplitArchers(team));
                                }
                                if (___AttackerLeaderBattleCombatant?.BasicCulture?.StringId == "khuzait")
                                {
                                    team.AddTacticOption(new TacticRangedHarrassmentOffensive(team));
                                }
                                //if (___AttackerLeaderBattleCombatant?.BasicCulture?.Id.ToString() == "vlandia")
                                //{
                                //    team.AddTacticOption(new TacticFrontalCavalryCharge(team));
                                //}
                                team.AddTacticOption(new TacticFullScaleAttack(team));
                                team.AddTacticOption(new TacticCoordinatedRetreat(team));
                            }
                            if (team.Side == BattleSideEnum.Defender)
                            {
                                team.ClearTacticOptions();
                                if (___DefenderLeaderBattleCombatant?.BasicCulture?.StringId == "battania")
                                {
                                    team.AddTacticOption(new RBMTacticDefendSplitArchers(team));
                                }
                                team.AddTacticOption(new TacticDefensiveEngagement(team));
                                team.AddTacticOption(new TacticDefensiveLine(team));
                                if (___DefenderLeaderBattleCombatant?.BasicCulture?.StringId == "sturgia" || ___DefenderLeaderBattleCombatant?.BasicCulture?.StringId == "nord")
                                {
                                    team.AddTacticOption(new RBMTacticDefendSplitInfantry(team));
                                }
                                team.AddTacticOption(new TacticFullScaleAttack(team));
                                team.AddTacticOption(new TacticCoordinatedRetreat(team));
                            }
                        }
                    }
                }
            }
        }
    }
}
