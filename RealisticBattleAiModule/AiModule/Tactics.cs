using HarmonyLib;
using SandBox.Missions.MissionLogics;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.ViewModelCollection.HUD.FormationMarker;
using static TaleWorlds.Core.ItemObject;

namespace RBMAI
{
    public static class Tactics
    {

        public class AIDecision
        {
            public int cooldown = 0;
            public WorldPosition position = WorldPosition.Invalid;
            public int customMaxCoolDown = -1;
        }

        public class AgentDamageDone
        {
            public float damageDone = 0f;
            public FormationClass initialClass = FormationClass.Unset;
            public bool isAttacker = false;
        }

        public static Dictionary<Agent, AIDecision> aiDecisionCooldownDict = new Dictionary<Agent, AIDecision>();
        public static Dictionary<Agent, AgentDamageDone> agentDamage = new Dictionary<Agent, AgentDamageDone>();


        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("OnAgentHit")]
        class CustomBattleAgentLogicOnAgentHitPatch
        {
            static void Postfix(Agent affectedAgent, Agent affectorAgent, in Blow b, in AttackCollisionData collisionData, bool isBlocked, float damagedHp)
            {
                if (affectedAgent != null && affectorAgent != null && affectedAgent.IsActive() && affectedAgent.IsHuman && !collisionData.AttackBlockedWithShield)
                {
                    if (!affectorAgent.IsHuman && affectorAgent.RiderAgent != null)
                    {
                        affectorAgent = affectorAgent.RiderAgent;
                    }
                    if(affectorAgent != null && affectorAgent.Team != null)
                    {
                        AgentDamageDone damageDone;
                        if (agentDamage.TryGetValue(affectorAgent, out damageDone))
                        {
                            agentDamage[affectorAgent].damageDone += damagedHp;
                        }
                        else
                        {
                            AgentDamageDone add = new AgentDamageDone();
                            if (affectorAgent.IsRangedCached && !affectorAgent.HasMount)
                            {
                                add.initialClass = FormationClass.Ranged;
                            }
                            else if (affectorAgent.IsRangedCached && affectorAgent.HasMount)
                            {
                                add.initialClass = FormationClass.HorseArcher;
                            }
                            else if (!affectorAgent.IsRangedCached && affectorAgent.HasMount)
                            {
                                add.initialClass = FormationClass.Cavalry;
                            }
                            else if (!affectorAgent.IsRangedCached && !affectorAgent.HasMount && affectorAgent.IsHuman)
                            {
                                add.initialClass = FormationClass.Infantry;
                            }
                            add.isAttacker = affectorAgent.Team.IsAttacker;
                            add.damageDone = damagedHp;
                            agentDamage[affectorAgent] = add;
                        }
                    }
                    
                }
            }
        }

        [HarmonyPatch(typeof(Team))]
        [HarmonyPatch("Tick")]
        class OverrideTick
        {

            static int i = 0;

            static void Postfix(Team __instance)
            {
                //if (__instance.Banner != null)
                //{
                //    if (i == 300)
                //    {
                //        if (__instance.IsAttacker)
                //        {
                //            float archersDamageDone = 0;
                //            float haDamageDone = 0;
                //            float cavDamageDone = 0;
                //            float infDamageDone = 0;
                //            foreach (KeyValuePair<Agent, AgentDamageDone> entry in agentDamage)
                //            {
                //                if (entry.Value.isAttacker)
                //                {
                //                    if (entry.Value.initialClass == FormationClass.Ranged)
                //                    {
                //                        archersDamageDone += entry.Value.damageDone;
                //                    }
                //                    if (entry.Value.initialClass == FormationClass.HorseArcher)
                //                    {
                //                        haDamageDone += entry.Value.damageDone;
                //                    }
                //                    if (entry.Value.initialClass == FormationClass.Cavalry)
                //                    {
                //                        cavDamageDone += entry.Value.damageDone;
                //                    }
                //                    if (entry.Value.initialClass == FormationClass.Infantry)
                //                    {
                //                        infDamageDone += entry.Value.damageDone;
                //                    }
                //                }
                //            }
                //            InformationManager.DisplayMessage(new InformationMessage("ATK ARC:" + archersDamageDone));
                //            InformationManager.DisplayMessage(new InformationMessage("ATK HA :" + haDamageDone));
                //            InformationManager.DisplayMessage(new InformationMessage("ATK CAV:" + cavDamageDone));
                //            InformationManager.DisplayMessage(new InformationMessage("ATK INF:" + infDamageDone));
                //        }
                //        else
                //        {
                //            float archersDamageDone = 0;
                //            float haDamageDone = 0;
                //            float cavDamageDone = 0;
                //            float infDamageDone = 0;
                //            foreach (KeyValuePair<Agent, AgentDamageDone> entry in agentDamage)
                //            {
                //                if (!entry.Value.isAttacker)
                //                {
                //                    if (entry.Value.initialClass == FormationClass.Ranged)
                //                    {
                //                        archersDamageDone += entry.Value.damageDone;
                //                    }
                //                    if (entry.Value.initialClass == FormationClass.HorseArcher)
                //                    {
                //                        haDamageDone += entry.Value.damageDone;
                //                    }
                //                    if (entry.Value.initialClass == FormationClass.Cavalry)
                //                    {
                //                        cavDamageDone += entry.Value.damageDone;
                //                    }
                //                    if (entry.Value.initialClass == FormationClass.Infantry)
                //                    {
                //                        infDamageDone += entry.Value.damageDone;
                //                    }
                //                }
                //            }
                //            InformationManager.DisplayMessage(new InformationMessage("DEF ARC:" + archersDamageDone));
                //            InformationManager.DisplayMessage(new InformationMessage("DEF HA :" + haDamageDone));
                //            InformationManager.DisplayMessage(new InformationMessage("DEF CAV:" + cavDamageDone));
                //            InformationManager.DisplayMessage(new InformationMessage("DEF INF:" + infDamageDone));
                //        }
                //        i = 0;
                //    }
                //    else
                //    {
                //        i++;
                //    }
                //}
                    //    if (__instance.IsAttacker)
                    //    {
                    //        FieldInfo _currentTacticField = typeof(TeamAIComponent).GetField("_currentTactic", BindingFlags.NonPublic | BindingFlags.Instance);
                    //        _currentTacticField.DeclaringType.GetField("_currentTactic");
                    //        TacticComponent _currentTactic = (TacticComponent)_currentTacticField.GetValue(__instance.TeamAI);

                    //        //InformationManager.DisplayMessage(new InformationMessage("Attacker " + __instance.TeamIndex + " : " + _currentTactic));

                    //        FieldInfo _availableTacticsField = typeof(TeamAIComponent).GetField("_availableTactics", BindingFlags.NonPublic | BindingFlags.Instance);
                    //        _availableTacticsField.DeclaringType.GetField("_availableTactics");
                    //        List<TacticComponent> _availableTactics = (List<TacticComponent>)_availableTacticsField.GetValue(__instance.TeamAI);
                    //        foreach (TacticComponent tc in _availableTactics)
                    //        {
                    //            MethodInfo method = typeof(TacticComponent).GetMethod("GetTacticWeight", BindingFlags.NonPublic | BindingFlags.Instance);
                    //            method.DeclaringType.GetMethod("GetTacticWeight");
                    //            float weight = (float)method.Invoke(tc, new object[] { });
                    //            //InformationManager.DisplayMessage(new InformationMessage(tc + ": " + weight));
                    //        }

                    //        foreach (Formation formation in __instance.Formations)
                    //        {
                    //            if (formation.QuerySystem.IsMeleeFormation)
                    //            {
                    //                //InformationManager.DisplayMessage(new InformationMessage("infantry: " + formation.AI.ActiveBehavior));
                    //            }
                    //            else if (formation.QuerySystem.IsCavalryFormation)
                    //            {
                    //                //InformationManager.DisplayMessage(new InformationMessage("cavalry: " + formation.AI.ActiveBehavior));
                    //            }
                    //            else if (formation.QuerySystem.IsRangedCavalryFormation)
                    //            {
                    //                InformationManager.DisplayMessage(new InformationMessage("HA:" + formation.AI.ActiveBehavior));
                    //            }
                    //            else if (formation.QuerySystem.IsRangedFormation)
                    //            {
                    //                //InformationManager.DisplayMessage(new InformationMessage("ranged: " + formation.AI.ActiveBehavior + " " + formation.QuerySystem.MissileRange));
                    //            }
                    //        }
                    //    }
                    //    else
                    //    {
                    //        FieldInfo _currentTacticField = typeof(TeamAIComponent).GetField("_currentTactic", BindingFlags.NonPublic | BindingFlags.Instance);
                    //        _currentTacticField.DeclaringType.GetField("_currentTactic");
                    //        TacticComponent _currentTactic = (TacticComponent)_currentTacticField.GetValue(__instance.TeamAI);

                    //        //InformationManager.DisplayMessage(new InformationMessage("Defender " + __instance.TeamIndex + " : " + _currentTactic));

                    //        FieldInfo _availableTacticsField = typeof(TeamAIComponent).GetField("_availableTactics", BindingFlags.NonPublic | BindingFlags.Instance);
                    //        _availableTacticsField.DeclaringType.GetField("_availableTactics");
                    //        List<TacticComponent> _availableTactics = (List<TacticComponent>)_availableTacticsField.GetValue(__instance.TeamAI);
                    //        foreach (TacticComponent tc in _availableTactics)
                    //        {
                    //            MethodInfo method = typeof(TacticComponent).GetMethod("GetTacticWeight", BindingFlags.NonPublic | BindingFlags.Instance);
                    //            method.DeclaringType.GetMethod("GetTacticWeight");
                    //            float weight = (float)method.Invoke(tc, new object[] { });
                    //            //InformationManager.DisplayMessage(new InformationMessage(tc + ": " + weight));
                    //        }

                    //        foreach (Formation formation in __instance.Formations)
                    //        {
                    //            if (formation.QuerySystem.IsMeleeFormation)
                    //            {
                    //                //InformationManager.DisplayMessage(new InformationMessage("infantry: " + formation.AI.ActiveBehavior));
                    //            }
                    //            else if (formation.QuerySystem.IsCavalryFormation)
                    //            {
                    //                //InformationManager.DisplayMessage(new InformationMessage("cavalry: " + formation.AI.ActiveBehavior));
                    //            }
                    //            else if (formation.QuerySystem.IsRangedCavalryFormation)
                    //            {
                    //                InformationManager.DisplayMessage(new InformationMessage("HA:" + formation.AI.ActiveBehavior));
                    //            }
                    //            else if (formation.QuerySystem.IsRangedFormation)
                    //            {
                    //                //InformationManager.DisplayMessage(new InformationMessage("ranged: " + formation.AI.ActiveBehavior + " " + formation.QuerySystem.MissileRange));
                    //            }
                    //        }
                    //    }
                    //    i = 0;
                    //}
                    //else
                    //{
                    //    i++;
                    //}
            }
        }

        //[HarmonyPatch(typeof(MissionAgentSpawnLogic))]
        //[HarmonyPatch("BattleSizeSpawnTick")]
        //class OverrideBattleSizeSpawnTick
        //{

        //    static bool Prefix(MissionAgentSpawnLogic __instance, int ____battleSize)
        //    {
        //        int numberOfActiveDefenderTroops = __instance.NumberOfActiveDefenderTroops;
        //        int numberOfActiveAttackerTroops = __instance.NumberOfActiveAttackerTroops;
        //        int numberOfActiveTroops = __instance.NumberOfActiveTroops;
        //        if ((float)numberOfActiveTroops < (float)____battleSize * 0.2f || (float)numberOfActiveDefenderTroops < (float)____battleSize * 0.2f || (float)numberOfActiveAttackerTroops < (float)____battleSize * 0.2f)
        //        {
        //            return true;
        //        }
        //        else
        //        {
        //            return false;
        //        }
        //    }
        //}

        [HarmonyPatch(typeof(TeamAIGeneral))]
        class OverrideTeamAIGeneral
        {

            [HarmonyPostfix]
            [HarmonyPatch("OnUnitAddedToFormationForTheFirstTime")]
            static void PostfixOnUnitAddedToFormationForTheFirstTime(Formation formation)
            {
                formation.AI.AddAiBehavior(new RBMBehaviorArcherSkirmish(formation));
                formation.AI.AddAiBehavior(new RBMBehaviorForwardSkirmish(formation));
                formation.AI.AddAiBehavior(new RBMBehaviorInfantryAttackFlank(formation));
                formation.AI.AddAiBehavior(new RBMBehaviorCavalryCharge(formation));
                formation.AI.AddAiBehavior(new RBMBehaviorEmbolon(formation));
                formation.AI.AddAiBehavior(new RBMBehaviorArcherFlank(formation));
                formation.AI.AddAiBehavior(new RBMBehaviorHorseArcherSkirmish(formation));
            }
        }

        [HarmonyPatch(typeof(MissionFormationMarkerTargetVM))]
        [HarmonyPatch("Refresh")]
        class OverrideRefresh
        {
            private static string chooseIcon(Formation formation)
            {
                if (formation != null)
                {
                    if (formation.QuerySystem.IsInfantryFormation)
                    {
                        return TargetIconType.Special_Swordsman.ToString();
                    }
                    if (formation.QuerySystem.IsRangedFormation)
                    {
                        return TargetIconType.Archer_Heavy.ToString();
                    }
                    if (formation.QuerySystem.IsRangedCavalryFormation)
                    {
                        return TargetIconType.HorseArcher_Light.ToString();
                    }
                    if (formation.QuerySystem.IsCavalryFormation && !RBMAI.Utilities.CheckIfMountedSkirmishFormation(formation, 0.6f))
                    {
                        return TargetIconType.Cavalry_Light.ToString();
                    }
                    if (formation.QuerySystem.IsCavalryFormation && RBMAI.Utilities.CheckIfMountedSkirmishFormation(formation, 0.6f))
                    {
                        return TargetIconType.Special_JavelinThrower.ToString();
                    }
                }
                return TargetIconType.None.ToString();
            }

            static void Postfix(MissionFormationMarkerTargetVM __instance)
            {
                __instance.FormationType = chooseIcon(__instance.Formation);
            }
        }

        [HarmonyPatch(typeof(CampaignMissionComponent))]
        [HarmonyPatch("EarlyStart")]
        public class CampaignMissionComponentPatch
        {
            public static void Postfix()
            {
                RBMAiPatcher.DoPatching();
                agentDamage.Clear();
            }
        }

        //[HarmonyPatch(typeof(MissionCombatantsLogic))]
        //[HarmonyPatch("AfterStart")]
        //public class AfterStartPatch
        //{
        //    public static void Postfix(ref IBattleCombatant ____attackerLeaderBattleCombatant)
        //    {
        //        if (Mission.Current.Teams.Any())
        //        {
        //            if (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle)
        //            {
        //                foreach (Team team in Mission.Current.Teams.Where((Team t) => t.HasTeamAi).ToList())
        //                {
        //                    if (team.Side == BattleSideEnum.Attacker)
        //                    {
        //                        if (____attackerLeaderBattleCombatant?.BasicCulture?.GetCultureCode() == CultureCode.Empire)
        //                        {
        //                            team.AddTacticOption(new RBMTacticEmbolon(team));
        //                        }
        //                        else
        //                        {
        //                            team.AddTacticOption(new TacticFrontalCavalryCharge(team));
        //                        }
        //                    }
        //                }
        //            }
        //        }
        //    }
        //}

        [HarmonyPatch(typeof(MissionCombatantsLogic))]
        [HarmonyPatch("EarlyStart")]
        public class EarlyStartPatch
        {
            public static void Postfix(ref IBattleCombatant ____attackerLeaderBattleCombatant, ref IBattleCombatant ____defenderLeaderBattleCombatant)
            {
                aiDecisionCooldownDict.Clear();
                agentDamage.Clear();
                RBMAiPatcher.DoPatching();
                OnTickAsAIPatch.itemPickupDistanceStorage.Clear();
                ManagedParameters.SetParameter(ManagedParametersEnum.BipedalRadius, 0.38f);
                if (Mission.Current.Teams.Any())
                {
                    if (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle)
                    {
                        foreach (Team team in Mission.Current.Teams.Where((Team t) => t.HasTeamAi).ToList())
                        {
                            if (team.Side == BattleSideEnum.Attacker)
                            {
                                team.ClearTacticOptions();
                                if (____attackerLeaderBattleCombatant?.BasicCulture?.GetCultureCode() == CultureCode.Empire)
                                {
                                    team.AddTacticOption(new RBMTacticEmbolon(team));
                                }
                                else
                                {
                                    //team.AddTacticOption(new TacticFrontalCavalryCharge(team));
                                }
                                if (____attackerLeaderBattleCombatant?.BasicCulture?.GetCultureCode() == CultureCode.Aserai)
                                {
                                    team.AddTacticOption(new RBMTacticAttackSplitSkirmishers(team));
                                }
                                if (____attackerLeaderBattleCombatant?.BasicCulture?.GetCultureCode() == CultureCode.Sturgia)
                                {
                                    team.AddTacticOption(new RBMTacticAttackSplitInfantry(team));
                                }
                                if (____attackerLeaderBattleCombatant?.BasicCulture?.GetCultureCode() == CultureCode.Battania)
                                {
                                    team.AddTacticOption(new RBMTacticAttackSplitArchers(team));
                                }
                                //if (____attackerLeaderBattleCombatant?.BasicCulture?.GetCultureCode() != CultureCode.Vlandia)
                                //{
                                //    team.AddTacticOption(new TacticRangedHarrassmentOffensive(team));
                                //}
                                team.AddTacticOption(new TacticFullScaleAttack(team));
                                //team.AddTacticOption(new RBMTacticEmbolon(team));
                                team.AddTacticOption(new TacticCoordinatedRetreat(team));
                                //team.AddTacticOption(new TacticCharge(team));
                                //team.AddTacticOption(new RBMTacticAttackSplitSkirmishers(team));
                                //team.AddTacticOption(new RBMTacticAttackSplitInfantry(team));
                            }
                            if (team.Side == BattleSideEnum.Defender)
                            {
                                team.ClearTacticOptions();
                                if (____defenderLeaderBattleCombatant?.BasicCulture?.GetCultureCode() == CultureCode.Battania)
                                {
                                    team.AddTacticOption(new RBMTacticDefendSplitArchers(team));
                                }
                                team.AddTacticOption(new TacticDefensiveEngagement(team));
                                team.AddTacticOption(new TacticDefensiveLine(team));
                                if(____defenderLeaderBattleCombatant?.BasicCulture?.GetCultureCode() == CultureCode.Sturgia)
                                {
                                    team.AddTacticOption(new RBMTacticDefendSplitInfantry(team));
                                }
                                team.AddTacticOption(new TacticFullScaleAttack(team));
                                //team.AddTacticOption(new TacticCharge(team));
                                //team.AddTacticOption(new TacticRangedHarrassmentOffensive(team));
                                //team.AddTacticOption(new TacticHoldChokePoint(team));
                                //team.AddTacticOption(new TacticHoldTheHill(team));
                                //team.AddTacticOption(new TacticRangedHarrassmentOffensive(team));
                                team.AddTacticOption(new TacticCoordinatedRetreat(team));
                                //team.AddTacticOption(new TacticFrontalCavalryCharge(team));
                                //team.AddTacticOption(new TacticDefensiveRing(team));
                                //team.AddTacticOption(new TacticArchersOnTheHill(team));
                            }
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(TacticCoordinatedRetreat))]
        class OverrideTacticCoordinatedRetreat
        {
            [HarmonyPrefix]
            [HarmonyPatch("GetTacticWeight")]
            static bool PrefixGetTacticWeight(ref TacticCoordinatedRetreat __instance, ref Team ___team, ref float __result)
            {
                //__result = 100f;
                
                if (___team.QuerySystem.InfantryRatio <= 0.1f && ___team.QuerySystem.RangedRatio <= 0.1f)
                {
                    float power = ___team.QuerySystem.TeamPower;
                    float enemyPower = ___team.QuerySystem.EnemyTeams.Sum((TeamQuerySystem et) => et.TeamPower);
                    if (power / enemyPower <= 0.15f)
                    {
                        foreach (Formation formation in ___team.Formations.ToList())
                        {
                            formation.AI.ResetBehaviorWeights();
                            formation.AI.SetBehaviorWeight<BehaviorRetreat>(100f);
                        }
                        __result = 1000f;
                        return false;
                    }
                    else
                    {
                        __result = 0f;
                    }
                }
                else
                {
                    __result = 0f;
                }
                return false;
            }
        }

        //[HarmonyPatch(typeof(MissionAgentSpawnLogic))]
        //class OverrideSetSpawnTroops
        //{
        //    [HarmonyPrefix]
        //    [HarmonyPatch("SetSpawnTroops")]
        //    static bool PrefixSetupTeams(ref MissionAgentSpawnLogic __instance,  BattleSideEnum side, bool spawnTroops, bool enforceSpawning = false)
        //    {
        //        return true;
        //    }
        //}

        [HarmonyPatch(typeof(TacticFrontalCavalryCharge))]
        class OverrideTacticFrontalCavalryCharge
        {

            [HarmonyPostfix]
            [HarmonyPatch("Advance")]
            static void PostfixAdvance(ref Formation ____cavalry)
            {
                if (____cavalry != null)
                {
                    ____cavalry.AI.SetBehaviorWeight<BehaviorVanguard>(1.5f);
                    ____cavalry.AI.SetBehaviorWeight<RBMBehaviorForwardSkirmish>(1f);
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch("Attack")]
            static void PostfixAttack(ref Formation ____mainInfantry, ref Formation ____cavalry, ref Formation ____archers)
            {

                if (____cavalry != null)
                {
                    ____cavalry.AI.ResetBehaviorWeights();
                    ____cavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    ____cavalry.AI.SetBehaviorWeight<RBMBehaviorCavalryCharge>(1f);
                    ____cavalry.AI.SetBehaviorWeight<RBMBehaviorForwardSkirmish>(1f);
                    //____cavalry.AI.SetBehaviorWeight<BehaviorTacticalCharge>(0f);
                    //____cavalry.AI.SetBehaviorWeight<BehaviorPullBack>(0f);
                }
                if (____archers != null)
                {
                    ____archers.AI.ResetBehaviorWeights();
                    ____archers.AI.SetBehaviorWeight<RBMBehaviorArcherSkirmish>(1f);
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(0f);
                }
                RBMAI.Utilities.FixCharge(ref ____mainInfantry);
            }

            [HarmonyPostfix]
            [HarmonyPatch("HasBattleBeenJoined")]
            static void PostfixHasBattleBeenJoined(Formation ____cavalry, bool ____hasBattleBeenJoined, ref bool __result)
            {
                __result = RBMAI.Utilities.HasBattleBeenJoined( ____cavalry, ____hasBattleBeenJoined, 125f);
            }

            //[HarmonyPostfix]
            //[HarmonyPatch("GetTacticWeight")]
            //static void PostfixGetAiWeight(TacticFrontalCavalryCharge __instance, ref float __result)
            //{
            //    FieldInfo teamField = typeof(TacticFrontalCavalryCharge).GetField("team", BindingFlags.NonPublic | BindingFlags.Instance);
            //    teamField.DeclaringType.GetField("team");
            //    Team currentTeam = (Team)teamField.GetValue(__instance);
            //        if (currentTeam.QuerySystem.CavalryRatio > 0.1f)
            //        {
            //            __result = currentTeam.QuerySystem.CavalryRatio * 4f;
            //        }
            //}

            [HarmonyPostfix]
            [HarmonyPatch("GetTacticWeight")]
            static void PostfixGetTacticWeight(ref float __result)
            {
                __result *= 0.75f;
            }
        }

        [HarmonyPatch(typeof(TacticRangedHarrassmentOffensive))]
        class OverrideTacticRangedHarrassmentOffensive
        {

            [HarmonyPostfix]
            [HarmonyPatch("Advance")]
            static void PostfixAdvance(ref Formation ____archers, ref Formation ____mainInfantry, ref Formation ____rightCavalry, ref Formation ____leftCavalry, ref Formation ____rangedCavalry)
            {
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
                    TacticRangedHarrassmentOffensive.SetDefaultBehaviorWeights(____rangedCavalry);
                    ____rangedCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch("Attack")]
            static void PostfixAttack(ref Formation ____archers, ref Formation ____mainInfantry, ref Formation ____rightCavalry, ref Formation ____leftCavalry, ref Formation ____rangedCavalry)
            {

                if (____rightCavalry != null)
                {
                    ____rightCavalry.AI.ResetBehaviorWeights();
                    TacticComponent.SetDefaultBehaviorWeights(____rightCavalry);
                    ____rightCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    ____rightCavalry.AI.SetBehaviorWeight<RBMBehaviorCavalryCharge>(1f);
                }
                if (____leftCavalry != null)
                {
                    ____leftCavalry.AI.ResetBehaviorWeights();
                    TacticComponent.SetDefaultBehaviorWeights(____leftCavalry);
                    ____leftCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                    ____leftCavalry.AI.SetBehaviorWeight<RBMBehaviorCavalryCharge>(1f);
                }
                if (____rangedCavalry != null)
                {
                    ____rangedCavalry.AI.ResetBehaviorWeights();
                    TacticRangedHarrassmentOffensive.SetDefaultBehaviorWeights(____rangedCavalry);
                    ____rangedCavalry.AI.SetBehaviorWeight<BehaviorMountedSkirmish>(1f);
                }
                if (____archers != null)
                {
                    ____archers.AI.ResetBehaviorWeights();
                    ____archers.AI.SetBehaviorWeight<RBMBehaviorArcherSkirmish>(1f);
                    ____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
                    ____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(0f);
                }
                RBMAI.Utilities.FixCharge(ref ____mainInfantry);
            }

            [HarmonyPostfix]
            [HarmonyPatch("HasBattleBeenJoined")]
            static void PostfixHasBattleBeenJoined(Formation ____mainInfantry, bool ____hasBattleBeenJoined, ref bool __result)
            {
                __result = RBMAI.Utilities.HasBattleBeenJoined( ____mainInfantry, ____hasBattleBeenJoined);
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

            [HarmonyPostfix]
            [HarmonyPatch("GetTacticWeight")]
            static void PostfixGetTacticWeight(ref TacticRangedHarrassmentOffensive __instance, ref Team ___team, ref float __result)
            {
                if(___team?.Leader?.Character?.Culture?.GetCultureCode() == CultureCode.Khuzait)
                {
                    __result *= 1.1f;
                }
                else
                {
                    __result *= 0.6f;
                }
            }
        }

        [HarmonyPatch(typeof(TacticComponent))]
        class OverrideTacticComponent
        {
            [HarmonyPrefix]
            [HarmonyPatch("SetDefaultBehaviorWeights")]
            static bool PrefixSetDefaultBehaviorWeights(ref Formation f)
            {
                if (f != null)
                {
                    if (f.QuerySystem.IsRangedFormation)
                    {
                        f.AI.SetBehaviorWeight<BehaviorCharge>(0.2f);
                    }
                    else
                    {
                        f.AI.SetBehaviorWeight<BehaviorCharge>(1f);
                    }
                    f.AI.SetBehaviorWeight<BehaviorPullBack>(0f);
                    f.AI.SetBehaviorWeight<BehaviorStop>(0f);
                    f.AI.SetBehaviorWeight<BehaviorReserve>(0f);
                }
                return false;
            }
        }

        
    }
}