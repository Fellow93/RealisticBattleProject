using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ItemObject;

namespace RealisticBattleAiModule
{
    public static class Utilities
    {

        public static bool HasBattleBeenJoined(Formation mainInfantry, bool hasBattleBeenJoined, float battleJoinRange)
        {
            if (mainInfantry != null)
            {
                if (FormationFightingInMelee(mainInfantry))
                {
                    mainInfantry.FiringOrder = FiringOrder.FiringOrderHoldYourFire;
                    return true;
                }
                else
                {
                    if(mainInfantry.QuerySystem.ClosestEnemyFormation != null && mainInfantry.QuerySystem.ClosestEnemyFormation.Formation != null)
                    {
                        Formation enemyForamtion = Utilities.FindSignificantEnemy(mainInfantry, true, true, false, false, false);
                        if (enemyForamtion != null)
                        {
                            float distanceSpeedValue = mainInfantry.QuerySystem.MedianPosition.AsVec2.Distance(enemyForamtion.QuerySystem.MedianPosition.AsVec2) / enemyForamtion.QuerySystem.MovementSpeedMaximum;
                            if (distanceSpeedValue <= 5f)
                            {
                                mainInfantry.FiringOrder = FiringOrder.FiringOrderHoldYourFire;
                            }
                            return (distanceSpeedValue <= (battleJoinRange + (hasBattleBeenJoined ? 5f : 0f)));
                        }
                    }
                    
                }
            }
            return true;
        }

        public static void FixCharge(ref Formation formation)
        {
            if (formation != null)
            {
                formation.AI.ResetBehaviorWeights();
                formation.AI.SetBehaviorWeight<BehaviorCharge>(1f);
            }
        }

        public static bool CheckIfMountedSkirmishFormation(Formation formation)
        {
            if (formation != null && formation.QuerySystem.IsCavalryFormation)
            {
                int mountedSkirmishersCount = 0;
                formation.ApplyActionOnEachUnit(delegate (Agent agent)
                {
                    bool ismountedSkrimisher = false;
                    for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                    {
                        if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                        {
                            if (agent.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Thrown && agent.Equipment[equipmentIndex].Amount > 0 && agent.MountAgent != null)
                            {
                                ismountedSkrimisher = true;
                            }
                        }
                    }
                    if (ismountedSkrimisher)
                    {
                        mountedSkirmishersCount++;
                    }
                });

                float mountedSkirmishersRatio = (float)mountedSkirmishersCount / (float)formation.CountOfUnits;
                if (mountedSkirmishersRatio > 0.6f)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }else
            {
                return false;
            }
        }

        public static bool CheckIfTwoHandedPolearmInfantry(Agent agent)
        {
            for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
            {
                if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                {
                    if (agent.Equipment[equipmentIndex].Item.PrimaryWeapon.WeaponClass == WeaponClass.TwoHandedPolearm)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static int GetHarnessTier(Agent agent)
        {
            int tier = -1;
            EquipmentElement equipmentElement = agent.SpawnEquipment[EquipmentIndex.HorseHarness];
            if (equipmentElement.Item != null)
            {
                tier = (int) equipmentElement.Item.Tier;
            }
            return tier;
        }

        public static Agent NearestAgentFromFormation(Vec2 unitPosition, Formation targetFormation)
        {
            Agent targetAgent = null;
            float distance = 10000f;
            targetFormation.ApplyActionOnEachUnit(delegate (Agent agent)
            {
                float newDist = unitPosition.Distance(agent.GetWorldPosition().AsVec2);
                if (newDist < distance)
                {
                    targetAgent = agent;
                    distance = newDist;
                }
            });
            return targetAgent;
        }

        public static Agent NearestAgentFromMultipleFormations(Vec2 unitPosition, List<Formation> formations)
        {
            Agent targetAgent = null;
            float distance = 10000f;
            foreach(Formation formation in formations.ToList())
            {
                formation.ApplyActionOnEachUnit(delegate (Agent agent)
                {
                    float newDist = unitPosition.Distance(agent.GetWorldPosition().AsVec2);
                    if (newDist < distance)
                    {
                        targetAgent = agent;
                        distance = newDist;
                    }
                });
            }
            return targetAgent;
        }

        public static Agent NearestEnemyAgent(Agent unit)
        {
            Agent targetAgent = null;
            float distance = 10000f;
            Vec2 unitPosition = unit.GetWorldPosition().AsVec2;
            foreach (Team team in Mission.Current.Teams.ToList())
            {
                if (team.IsEnemyOf(unit.Formation.Team))
                {
                    foreach (Formation enemyFormation in team.Formations.ToList())
                    {
                        enemyFormation.ApplyActionOnEachUnit(delegate (Agent agent)
                        {
                            float newDist = unitPosition.Distance(agent.GetWorldPosition().AsVec2);
                            if (newDist < distance)
                            {
                                targetAgent = agent;
                                distance = newDist;
                            }
                        });
                    }
                }
            }
            return targetAgent;
        }

        public static bool FormationFightingInMelee(Formation formation)
        {
            bool fightingInMelee = false;           
            float currentTime = MBCommon.TimeType.Mission.GetTime();
            float countOfUnits = 0;
            float countOfUnitsFightingInMelee = 0;
            formation.ApplyActionOnEachUnit(delegate (Agent agent)
            {
                if (agent != null)
                {
                    countOfUnits++;
                    float lastMeleeAttackTime = agent.LastMeleeAttackTime;
                    float lastMeleeHitTime = agent.LastMeleeHitTime;
                    if ((currentTime - lastMeleeAttackTime < 4f) || (currentTime - lastMeleeHitTime < 4f))
                    {
                        countOfUnitsFightingInMelee++;
                    }
                }
            });
            if (countOfUnitsFightingInMelee / countOfUnits >= 0.5f)
            {
                fightingInMelee = true;
            }
            return fightingInMelee;
        }

        public static List<Formation> FindSignificantFormations(Formation formation)
        {
            List<Formation> formations = new List<Formation>();
            if (formation != null)
            {
                if (formation.QuerySystem.ClosestEnemyFormation != null)
                {
                    foreach (Team team in Mission.Current.Teams.ToList())
                    {
                        if (team.IsEnemyOf(formation.Team))
                        {
                            if (team.Formations.ToList().Count == 1)
                            {
                                formations.Add(team.Formations.ToList()[0]);
                                return formations;
                            }
                            foreach (Formation enemyFormation in team.Formations.ToList())
                            {
                                if (enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsInfantryFormation)
                                {
                                     formations.Add(enemyFormation);
                                }
                                if (enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsRangedFormation)
                                {
                                    formations.Add(enemyFormation);
                                }
                            }
                        }
                    }
                }
            }
            return formations;
        }

        public static Formation FindSignificantEnemy(Formation formation, bool includeInfantry, bool includeRanged, bool includeCavalry, bool includeMountedSkirmishers, bool includeHorseArchers)
        {
            Formation significantEnemy = null;
            float dist = 10000f;
            if(formation != null)
            {
                if (formation.QuerySystem.ClosestEnemyFormation != null)
                {
                    foreach (Team team in Mission.Current.Teams.ToList())
                    {
                        if (team.IsEnemyOf(formation.Team))
                        {
                            if(team.Formations.ToList().Count == 1)
                            {
                                significantEnemy = team.Formations.ToList()[0];
                                return significantEnemy;
                            }
                            foreach (Formation enemyFormation in team.Formations.ToList())
                            {
                                if (formation != null && includeInfantry && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsInfantryFormation)
                                {
                                    float newDist = formation.QuerySystem.MedianPosition.AsVec2.Distance(enemyFormation.QuerySystem.MedianPosition.AsVec2);
                                    if (newDist < dist)
                                    {
                                        significantEnemy = enemyFormation;
                                        dist = newDist;
                                    }
                                }
                                if (formation != null && includeRanged && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsRangedFormation)
                                {
                                    float newDist = formation.QuerySystem.MedianPosition.AsVec2.Distance(enemyFormation.QuerySystem.MedianPosition.AsVec2);
                                    if (newDist < dist)
                                    {
                                        significantEnemy = enemyFormation;
                                        dist = newDist;
                                    }
                                }
                                //if (formation != null && includeCavalry && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsCavalryFormation && !CheckIfMountedSkirmishFormation(enemyFormation) && !enemyFormation.QuerySystem.IsRangedCavalryFormation)
                                //{
                                //    float newDist = formation.QuerySystem.MedianPosition.AsVec2.Distance(enemyFormation.QuerySystem.MedianPosition.AsVec2);
                                //    if (newDist < dist)
                                //    {
                                //        significantEnemy = enemyFormation;
                                //        dist = newDist;
                                //    }
                                //}
                                //if (formation != null && includeMountedSkirmishers && enemyFormation.CountOfUnits > 0 && CheckIfMountedSkirmishFormation(enemyFormation))
                                //{
                                //    float newDist = formation.QuerySystem.MedianPosition.AsVec2.Distance(enemyFormation.QuerySystem.MedianPosition.AsVec2);
                                //    if (newDist < dist)
                                //    {
                                //        significantEnemy = enemyFormation;
                                //        dist = newDist;
                                //    }
                                //}
                                //if (formation != null && includeHorseArchers && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsRangedCavalryFormation)
                                //{
                                //    float newDist = formation.QuerySystem.MedianPosition.AsVec2.Distance(enemyFormation.QuerySystem.MedianPosition.AsVec2);
                                //    if (newDist < dist)
                                //    {
                                //        significantEnemy = enemyFormation;
                                //        dist = newDist;
                                //    }
                                //}
                            }
                        }
                    }
                }
            }
            return significantEnemy;
        }

        public static float GetCombatAIDifficultyMultiplier()
        {
            switch (CampaignOptions.CombatAIDifficulty)
            {
                case CampaignOptions.Difficulty.VeryEasy:
                    return 0.70f;
                case CampaignOptions.Difficulty.Easy:
                    return 0.85f;
                case CampaignOptions.Difficulty.Realistic:
                    return 1.0f;
                default:
                    return 1.0f;
            }
        }

        public static float CalculateAILevel(Agent agent, int relevantSkillLevel)
        {
            float difficultyModifier = GetCombatAIDifficultyMultiplier();
            //float difficultyModifier = 1.0f; // v enhanced battle test je difficulty very easy
            return MBMath.ClampFloat((float)relevantSkillLevel / 250f * difficultyModifier, 0f, 1f);
        }

        public static int GetMeleeSkill(Agent agent, WeaponComponentData equippedItem, WeaponComponentData secondaryItem)
        {
            SkillObject skill = DefaultSkills.Athletics;
            if (equippedItem != null)
            {
                SkillObject relevantSkill = equippedItem.RelevantSkill;
                skill = ((relevantSkill == DefaultSkills.OneHanded || relevantSkill == DefaultSkills.Polearm) ? relevantSkill : ((relevantSkill != DefaultSkills.TwoHanded) ? DefaultSkills.OneHanded : ((secondaryItem == null) ? DefaultSkills.TwoHanded : DefaultSkills.OneHanded)));
            }
            return GetEffectiveSkill(agent.Character, agent.Origin, agent.Formation, skill);
        }

        public static int GetEffectiveSkill(BasicCharacterObject agentCharacter, IAgentOriginBase agentOrigin, Formation agentFormation, SkillObject skill)
        {
            return agentCharacter.GetSkillValue(skill);
        }

        public static float sign(Vec2 p1, Vec2 p2, Vec2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }

        public static bool PointInTriangle(Vec2 pt, Vec2 v1, Vec2 v2, Vec2 v3)
        {
            float d1, d2, d3;
            bool has_neg, has_pos;

            d1 = sign(pt, v1, v2);
            d2 = sign(pt, v2, v3);
            d3 = sign(pt, v3, v1);

            has_neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            has_pos = (d1 > 0) || (d2 > 0) || (d3 > 0);

            return !(has_neg && has_pos);
        }
    }
}

