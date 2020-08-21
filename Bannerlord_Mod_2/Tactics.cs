using HarmonyLib;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace RealisticBattle
{
    //TODO: DEFENSE FIX
    class Tactics
    {
        /*
        [HarmonyPatch(typeof(Team))]
        [HarmonyPatch("Tick")]
        class OverrideTick
        {

            static int i = 0;

            static void Postfix(Team __instance)
            {
                if(i == 10)
                {
                    if (__instance.IsAttacker)
                    {
                        InformationManager.DisplayMessage(new InformationMessage("Attacker"));
                        //InformationManager.DisplayMessage(new InformationMessage(__instance.TeamAI));TacticComponent _currentTactic
                        //foreach
                    }
                    else
                    {
                        InformationManager.DisplayMessage(new InformationMessage("Defender"));
                    }
                    i = 0;
                }
                else
                {
                    i++;
                }
            }
        }
        */
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
                                team.AddTacticOption(new TacticCharge(team));
                            }
                            if (team.Side == BattleSideEnum.Defender)
                            {
                                team.ClearTacticOptions();
                                team.AddTacticOption(new TacticDefensiveEngagement(team));
                                team.AddTacticOption(new TacticDefensiveLine(team));
                                team.AddTacticOption(new TacticHoldChokePoint(team));
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
            static void PostfixAdvance(ref Formation ____mainInfantry, ref Formation ____archers)
            {
                if (____mainInfantry != null)
                {
                    ____mainInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(2f);
                }
                if (____archers != null)
                {
                    ____archers.AI.SetBehaviorWeight<BehaviorRegroup>(2f);
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch("Attack")]
            static void PostfixAttack(ref Formation ____mainInfantry)
            {
                Utilities.FixCharge(ref ____mainInfantry);
            }

            [HarmonyPostfix]
            [HarmonyPatch("HasBattleBeenJoined")]
            static void PostfixHasBattleBeenJoined(Formation ____mainInfantry, bool ____hasBattleBeenJoined, ref bool __result)
            {
                __result = Utilities.HasBattleBeenJoined(____mainInfantry, ____hasBattleBeenJoined, 12f);
            }
        }

        [HarmonyPatch(typeof(TacticFrontalCavalryCharge))]
        class OverrideTacticFrontalCavalryCharge
        {

            [HarmonyPostfix]
            [HarmonyPatch("Attack")]
            static void PostfixAttack(ref Formation ____mainInfantry)
            {
                Utilities.FixCharge(ref ____mainInfantry);
            }

            [HarmonyPostfix]
            [HarmonyPatch("HasBattleBeenJoined")]
            static void PostfixHasBattleBeenJoined(Formation ____cavalry, bool ____hasBattleBeenJoined, ref bool __result)
            {
                __result = Utilities.HasBattleBeenJoined(____cavalry, ____hasBattleBeenJoined, 7f);
            }
        }

        [HarmonyPatch(typeof(TacticRangedHarrassmentOffensive))]
        class OverrideTacticRangedHarrassmentOffensive
        {
            [HarmonyPostfix]
            [HarmonyPatch("Attack")]
            static void PostfixAttack(ref Formation ____mainInfantry)
            {
                Utilities.FixCharge(ref ____mainInfantry);
            }

            [HarmonyPostfix]
            [HarmonyPatch("HasBattleBeenJoined")]
            static void PostfixHasBattleBeenJoined(Formation ____mainInfantry, bool ____hasBattleBeenJoined, ref bool __result)
            {
                __result = Utilities.HasBattleBeenJoined(____mainInfantry, ____hasBattleBeenJoined, 12f);
            }
        }

        [HarmonyPatch(typeof(TacticDefensiveLine))]
        class OverrideTacticDefensiveLine
        {
            [HarmonyPostfix]
            [HarmonyPatch("HasBattleBeenJoined")]
            static void PostfixHasBattleBeenJoined(Formation ____mainInfantry, bool ____hasBattleBeenJoined, ref bool __result)
            {
                __result = Utilities.HasBattleBeenJoined(____mainInfantry, ____hasBattleBeenJoined, 12f);
            }

            [HarmonyPostfix]
            [HarmonyPatch("Defend")]
            static void PostfixDefend(ref Formation ____archers, ref Formation ____mainInfantry)
            {
                if (____archers != null)
                {
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
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmish>(0f);
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
                __result = Utilities.HasBattleBeenJoined(____mainInfantry, ____hasBattleBeenJoined, 12f);
            }

            [HarmonyPostfix]
            [HarmonyPatch("Defend")]
            static void PostfixDefend(ref Formation ____archers, ref Formation ____mainInfantry)
            {
                if (____archers != null)
                {
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
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmish>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
                }

                Utilities.FixCharge(ref ____mainInfantry);
            }
        }
    }
}