using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ItemObject;

namespace RBMAI
{
    public static class Utilities
    {

        public static bool HasBattleBeenJoined(Formation mainInfantry, bool hasBattleBeenJoined, float battleJoinRange = 75f)
        {
            bool isOnlyCavReamining = CheckIfOnlyCavRemaining(mainInfantry);
            if (isOnlyCavReamining)
            {
                return true;
            }
            if (mainInfantry != null)
            {
                //if (FormationFightingInMelee(mainInfantry, 0.4f))
                //{
                //    if (mainInfantry.IsAIControlled)
                //    {
                //        mainInfantry.FiringOrder = FiringOrder.FiringOrderHoldYourFire;
                //        return true;
                //    }
                //}
                //else
                //{
                //    if (mainInfantry.IsAIControlled)
                //    {
                //        mainInfantry.FiringOrder = FiringOrder.FiringOrderFireAtWill;
                //    }
                //}
                if (mainInfantry.QuerySystem.ClosestEnemyFormation != null && mainInfantry.QuerySystem.ClosestEnemyFormation.Formation != null)
                {
                    Formation enemyForamtion = RBMAI.Utilities.FindSignificantEnemy(mainInfantry, true, true, false, false, false, true);
                    if (enemyForamtion != null)
                    {
                        //float distanceSpeedValue = mainInfantry.QuerySystem.MedianPosition.AsVec2.Distance(enemyForamtion.QuerySystem.MedianPosition.AsVec2) / enemyForamtion.QuerySystem.MovementSpeedMaximum;
                        float distance = mainInfantry.QuerySystem.MedianPosition.AsVec2.Distance(enemyForamtion.QuerySystem.MedianPosition.AsVec2);
                        //{
                        //    mainInfantry.FiringOrder = FiringOrder.FiringOrderHoldYourFire;
                        //}
                        return (distance <= (battleJoinRange + (hasBattleBeenJoined ? 5f : 0f)));
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

        public static bool CheckIfMountedSkirmishFormation(Formation formation, float desiredRatio)
        {
            if (formation != null && formation.QuerySystem.IsCavalryFormation)
            {
                float ratio = 0f;
                int mountedSkirmishersCount = 0;
                int countedUnits = 0;
                formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                {
                    bool ismountedSkrimisher = false;
                    if (ratio <= desiredRatio && ((float)countedUnits / (float)formation.CountOfUnits) <= desiredRatio)
                    {
                        for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                        {
                            if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                            {
                                if (agent.MountAgent != null && agent.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Thrown && agent.Equipment[equipmentIndex].Amount > 0)
                                {
                                    ismountedSkrimisher = true;
                                    break;
                                }
                            }
                        }
                    }
                    if (ismountedSkrimisher)
                    {
                        mountedSkirmishersCount++;
                    }
                    countedUnits++;
                    ratio = (float)mountedSkirmishersCount / (float)formation.CountOfUnits;
                });

                if (ratio > desiredRatio)
                {
                    return true;
                }
            }
            return false;
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
                    else if (agent.Equipment[equipmentIndex].Item.PrimaryWeapon.WeaponClass == WeaponClass.TwoHandedMace)
                    {
                        return true;
                    }
                    else if (agent.Equipment[equipmentIndex].Item.PrimaryWeapon.WeaponClass == WeaponClass.TwoHandedSword)
                    {
                        return true;
                    }
                    else if (agent.Equipment[equipmentIndex].Item.PrimaryWeapon.WeaponClass == WeaponClass.TwoHandedAxe)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            return false;
        }

        public static int GetHarnessTier(Agent agent)
        {
            int tier = 10;
            EquipmentElement equipmentElement = agent.SpawnEquipment[EquipmentIndex.HorseHarness];
            if (agent != null)
            {
                if (agent.MountAgent != null)
                {
                    if (agent.SpawnEquipment != null)
                    {
                        if (equipmentElement.Item != null)
                        {
                            if (Game.Current.BasicModels.ItemValueModel != null)
                            {
                                tier = (int)equipmentElement.Item.Tier;
                            }
                        }
                    }
                }
            }
            return tier;
        }

        public static Agent GetCorrectTarget(Agent agent)
        {
            List<Formation> formations;
            if (agent != null)
            {
                Formation formation = agent.Formation;
                if (formation != null)
                {
                    if ((formation.QuerySystem.IsInfantryFormation || formation.QuerySystem.IsRangedFormation) && (formation.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget))
                    {
                        formations = RBMAI.Utilities.FindSignificantFormations(formation);
                        if (formations.Count > 0)
                        {
                            return RBMAI.Utilities.NearestAgentFromMultipleFormations(agent.Position.AsVec2, formations);
                        }
                    }
                }
            }
            return null;
        }


        public static Agent NearestAgentFromFormation(Vec2 unitPosition, Formation targetFormation)
        {
            Agent targetAgent = null;
            float distance = 10000f;
            targetFormation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
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
            foreach (Formation formation in formations.ToList())
            {
                formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                {
                    if (agent.IsAIControlled)
                    {
                        if (agent.CommonAIComponent != null && agent.CommonAIComponent.IsRetreating)
                        {
                            float panick = agent.CommonAIComponent.Morale;
                        }
                        else
                        {
                            float newDist = unitPosition.Distance(agent.GetWorldPosition().AsVec2);
                            if (newDist < distance)
                            {
                                targetAgent = agent;
                                distance = newDist;
                            }
                        }
                    }
                    else
                    {
                        float newDist = unitPosition.Distance(agent.GetWorldPosition().AsVec2);
                            if (newDist < distance)
                            {
                                targetAgent = agent;
                                distance = newDist;
                            }
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
                        enemyFormation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
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

        public static bool FormationActiveSkirmishersRatio(Formation formation, float desiredRatio)
        {
            float ratio = 0f;
            int countOfSkirmishers = 0;
            if (formation != null)
            {
                formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                {
                    //float currentTime = MBCommon.TimeType.Mission.GetTime();
                    //if (currentTime - agent.LastRangedAttackTime < 6f)
                    //{
                    //    countOfSkirmishers++;
                    //}
                    bool isActiveSkrimisher = false;
                    float countedUnits = 0f;
                    float currentTime = MBCommon.GetTotalMissionTime();
                    if (agent.LastRangedAttackTime > 0f && currentTime - agent.LastRangedAttackTime < 9f && ratio <= desiredRatio && ((float)countedUnits / (float)formation.CountOfUnits) <= desiredRatio)
                    {
                        for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                        {
                            if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                            {
                                if (agent.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Thrown && agent.Equipment[equipmentIndex].Amount > 1)
                                {
                                    isActiveSkrimisher = true;
                                    break;
                                }
                            }
                        }
                    }
                    if (isActiveSkrimisher)
                    {
                        countOfSkirmishers++;
                    }
                    countedUnits++;
                    ratio = (float)countOfSkirmishers / (float)formation.CountOfUnits;

                });
                if (ratio > desiredRatio)
                {
                    return true;
                }
            }
            return false;
        }

        public static bool FormationFightingInMelee(Formation formation, float desiredRatio)
        {
            float currentTime = MBCommon.GetTotalMissionTime();
            float countedUnits = 0;
            float ratio = 0f;
            float countOfUnitsFightingInMelee = 0;
            formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
            {
                if (agent != null && ratio <= desiredRatio && ((float)countedUnits / (float)formation.CountOfUnits) <= desiredRatio)
                {
                    float lastMeleeAttackTime = agent.LastMeleeAttackTime;
                    float lastMeleeHitTime = agent.LastMeleeHitTime;
                    if ((currentTime - lastMeleeAttackTime < 4f) || (currentTime - lastMeleeHitTime < 4f))
                    {
                        countOfUnitsFightingInMelee++;
                    }
                    countedUnits++;
                }
            });
            if (countOfUnitsFightingInMelee / formation.CountOfUnits >= desiredRatio)
            {
                return true;
            }
            return false;
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
                                if (formation != null && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsInfantryFormation)
                                {
                                    formations.Add(enemyFormation);
                                }
                                if (formation != null && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsRangedFormation)
                                {
                                    formations.Add(enemyFormation);
                                }
                                //if (formation != null && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsCavalryFormation)
                                //{
                                //    formations.Add(enemyFormation);
                                //}
                            }
                        }
                    }
                }
            }
            return formations;
        }

        public static Formation FindSignificantEnemyToPosition(Formation formation, WorldPosition position, bool includeInfantry, bool includeRanged, bool includeCavalry, bool includeMountedSkirmishers, bool includeHorseArchers, bool withSide, bool unitCountMatters = false, float unitCountModifier = 1f)
        {
            Formation significantEnemy = null;
            List<Formation> significantFormations = new List<Formation>();
            float dist = 10000f;
            float significantTreshold = 0.6f;
            List<Formation> allEnemyFormations = new List<Formation>();

            if (formation != null)
            {

                if (formation.QuerySystem.ClosestEnemyFormation != null)
                {
                    foreach (Team team in Mission.Current.Teams.ToList())
                    {
                        if (team.IsEnemyOf(formation.Team))
                        {
                            foreach (Formation enemyFormation in team.Formations.ToList())
                            {
                                allEnemyFormations.Add(enemyFormation);
                            }
                        }
                    }

                    if (allEnemyFormations.ToList().Count == 1)
                    {
                        significantEnemy = allEnemyFormations[0];
                        return significantEnemy;
                    }

                    foreach (Formation enemyFormation in allEnemyFormations.ToList())
                    {
                        if (withSide)
                        {
                            if (formation.AI.Side != enemyFormation.AI.Side)
                            {
                                continue;
                            }
                        }
                        if (formation != null && includeInfantry && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsInfantryFormation)
                        {
                            float newDist = position.AsVec2.Distance(enemyFormation.QuerySystem.MedianPosition.AsVec2);
                            if (newDist < dist)
                            {
                                significantEnemy = enemyFormation;
                                dist = newDist;
                            }

                            float newUnitCountRatio = (enemyFormation.CountOfUnits * unitCountModifier) / formation.CountOfUnits;
                            if (unitCountMatters)
                            {
                                if (newUnitCountRatio > significantTreshold)
                                {
                                    significantFormations.Add(enemyFormation);
                                }
                            }
                        }
                        if (formation != null && includeRanged && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsRangedFormation)
                        {
                            float newDist = position.AsVec2.Distance(enemyFormation.QuerySystem.MedianPosition.AsVec2);
                            if (newDist < dist)
                            {
                                significantEnemy = enemyFormation;
                                dist = newDist;
                            }

                            float newUnitCountRatio = (enemyFormation.CountOfUnits * unitCountModifier) / formation.CountOfUnits;
                            if (unitCountMatters)
                            {
                                if (newUnitCountRatio > significantTreshold)
                                {
                                    significantFormations.Add(enemyFormation);
                                }
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

                    if (unitCountMatters)
                    {
                        if (significantFormations.Count > 0)
                        {
                            dist = 10000f;
                            foreach (Formation significantFormation in significantFormations)
                            {
                                float newDist = position.AsVec2.Distance(significantFormation.QuerySystem.MedianPosition.AsVec2);
                                if (newDist < dist)
                                {
                                    significantEnemy = significantFormation;
                                    dist = newDist;
                                }
                            }
                        }
                        else
                        {
                            dist = 10000f;
                            foreach (Formation significantFormation in allEnemyFormations)
                            {
                                float newDist = position.AsVec2.Distance(significantFormation.QuerySystem.MedianPosition.AsVec2);
                                if (newDist < dist)
                                {
                                    significantEnemy = significantFormation;
                                    dist = newDist;
                                }
                            }
                        }

                    }
                }
            }

            return significantEnemy;
        }

        public static Formation FindSignificantEnemy(Formation formation, bool includeInfantry, bool includeRanged, bool includeCavalry, bool includeMountedSkirmishers, bool includeHorseArchers, bool unitCountMatters = false, float unitCountModifier = 1f)
        {
            Formation significantEnemy = null;
            List<Formation> significantFormations = new List<Formation>();
            float dist = 10000f;
            float significantTreshold = 0.6f;
            List<Formation> allEnemyFormations = new List<Formation>();

            if (formation != null)
            {
                if (formation.QuerySystem.ClosestEnemyFormation != null)
                {
                    foreach (Team team in Mission.Current.Teams.ToList())
                    {
                        if (team.IsEnemyOf(formation.Team))
                        {
                            foreach (Formation enemyFormation in team.Formations.ToList())
                            {
                                allEnemyFormations.Add(enemyFormation);
                            }
                        }
                    }

                    if (allEnemyFormations.ToList().Count == 1)
                    {
                        significantEnemy = allEnemyFormations[0];
                        return significantEnemy;
                    }

                    foreach (Formation enemyFormation in allEnemyFormations.ToList())
                    {
                        if (formation != null && includeInfantry && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsInfantryFormation)
                        {
                            float newDist = formation.QuerySystem.MedianPosition.AsVec2.Distance(enemyFormation.QuerySystem.MedianPosition.AsVec2);
                            if (newDist < dist)
                            {
                                significantEnemy = enemyFormation;
                                dist = newDist;
                            }

                            float newUnitCountRatio = (enemyFormation.CountOfUnits * unitCountModifier) / formation.CountOfUnits;
                            if (unitCountMatters)
                            {
                                if (newUnitCountRatio > significantTreshold)
                                {
                                    significantFormations.Add(enemyFormation);
                                }
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

                            float newUnitCountRatio = (enemyFormation.CountOfUnits * unitCountModifier) / formation.CountOfUnits;
                            if (unitCountMatters)
                            {
                                if (newUnitCountRatio > significantTreshold)
                                {
                                    significantFormations.Add(enemyFormation);
                                }
                            }
                        }
                        if (formation != null && includeCavalry && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsCavalryFormation && !enemyFormation.QuerySystem.IsRangedCavalryFormation)
                        {
                            float newDist = formation.QuerySystem.MedianPosition.AsVec2.Distance(enemyFormation.QuerySystem.MedianPosition.AsVec2);
                            if (newDist < dist)
                            {
                                significantEnemy = enemyFormation;
                                dist = newDist;
                            }

                            float newUnitCountRatio = (enemyFormation.CountOfUnits * unitCountModifier) / formation.CountOfUnits;
                            if (unitCountMatters)
                            {
                                if (newUnitCountRatio > significantTreshold)
                                {
                                    significantFormations.Add(enemyFormation);
                                }
                            }
                        }
                        //if (formation != null && includeMountedSkirmishers && enemyFormation.CountOfUnits > 0 && CheckIfMountedSkirmishFormation(enemyFormation))
                        //{
                        //    float newDist = formation.QuerySystem.MedianPosition.AsVec2.Distance(enemyFormation.QuerySystem.MedianPosition.AsVec2);
                        //    if (newDist < dist)
                        //    {
                        //        significantEnemy = enemyFormation;
                        //        dist = newDist;
                        //    }
                        //}
                        if (formation != null && includeHorseArchers && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsRangedCavalryFormation)
                        {
                            float newDist = formation.QuerySystem.MedianPosition.AsVec2.Distance(enemyFormation.QuerySystem.MedianPosition.AsVec2);
                            if (newDist < dist)
                            {
                                significantEnemy = enemyFormation;
                                dist = newDist;
                            }

                            float newUnitCountRatio = (enemyFormation.CountOfUnits * unitCountModifier) / formation.CountOfUnits;
                            if (unitCountMatters)
                            {
                                if (newUnitCountRatio > significantTreshold)
                                {
                                    significantFormations.Add(enemyFormation);
                                }
                            }
                        }
                    }

                    if (unitCountMatters)
                    {
                        if (significantFormations.Count > 0)
                        {
                            dist = 10000f;
                            foreach (Formation significantFormation in significantFormations)
                            {
                                float newDist = formation.QuerySystem.MedianPosition.AsVec2.Distance(significantFormation.QuerySystem.MedianPosition.AsVec2);
                                if (newDist < dist)
                                {
                                    significantEnemy = significantFormation;
                                    dist = newDist;
                                }
                            }
                        }
                        else
                        {
                            dist = 10000f;
                            foreach (Formation enemyFormation in allEnemyFormations)
                            {
                                float newUnitCountRatio = (enemyFormation.CountOfUnits * unitCountModifier) / formation.CountOfUnits;
                                float newDist = formation.QuerySystem.MedianPosition.AsVec2.Distance(enemyFormation.QuerySystem.MedianPosition.AsVec2);
                                if (newDist < dist * newUnitCountRatio)
                                {
                                    significantEnemy = enemyFormation;
                                    dist = newDist;
                                }
                            }
                        }

                    }
                }
            }

            return significantEnemy;
        }

        public static bool CheckIfOnlyCavRemaining(Formation formation)
        {
            List<Formation> allEnemyFormations = new List<Formation>();
            bool result = true;
            if (formation != null)
            {
                if (formation.QuerySystem.ClosestEnemyFormation != null)
                {
                    foreach (Team team in Mission.Current.Teams.ToList())
                    {
                        if (team.IsEnemyOf(formation.Team))
                        {
                            foreach (Formation enemyFormation in team.Formations.ToList())
                            {
                                allEnemyFormations.Add(enemyFormation);
                            }
                        }
                    }

                    foreach (Formation enemyFormation in allEnemyFormations.ToList())
                    {
                        if(!enemyFormation.QuerySystem.IsCavalryFormation && !enemyFormation.QuerySystem.IsRangedCavalryFormation)
                        {
                            result = false;
                        }
                    }

                }
            }

            return result;
        }

        public static Formation FindSignificantAlly(Formation formation, bool includeInfantry, bool includeRanged, bool includeCavalry, bool includeMountedSkirmishers, bool includeHorseArchers)
        {
            Formation significantAlly = null;
            float dist = 10000f;
            if (formation != null)
            {
                if (formation.QuerySystem.ClosestEnemyFormation != null)
                {
                    foreach (Team team in Mission.Current.Teams.ToList())
                    {
                        if (!team.IsEnemyOf(formation.Team))
                        {
                            if (team.Formations.ToList().Count == 1)
                            {
                                significantAlly = team.Formations.ToList()[0];
                                return significantAlly;
                            }
                            foreach (Formation allyFormation in team.Formations.ToList())
                            {
                                if (formation != null && includeInfantry && allyFormation.CountOfUnits > 0 && allyFormation.QuerySystem.IsInfantryFormation)
                                {
                                    float newDist = formation.QuerySystem.MedianPosition.AsVec2.Distance(allyFormation.QuerySystem.MedianPosition.AsVec2);
                                    if (newDist < dist)
                                    {
                                        significantAlly = allyFormation;
                                        dist = newDist;
                                    }
                                }
                                if (formation != null && includeRanged && allyFormation.CountOfUnits > 0 && allyFormation.QuerySystem.IsRangedFormation)
                                {
                                    float newDist = formation.QuerySystem.MedianPosition.AsVec2.Distance(allyFormation.QuerySystem.MedianPosition.AsVec2);
                                    if (newDist < dist)
                                    {
                                        significantAlly = allyFormation;
                                        dist = newDist;
                                    }
                                }
                                if (formation != null && includeCavalry && allyFormation.CountOfUnits > 0 && allyFormation.QuerySystem.IsCavalryFormation && !allyFormation.QuerySystem.IsRangedCavalryFormation)
                                {
                                    float newDist = formation.QuerySystem.MedianPosition.AsVec2.Distance(allyFormation.QuerySystem.MedianPosition.AsVec2);
                                    if (newDist < dist)
                                    {
                                        significantAlly = allyFormation;
                                        dist = newDist;
                                    }
                                }
                                //if (formation != null && includeMountedSkirmishers && allyFormation.CountOfUnits > 0 && CheckIfMountedSkirmishFormation(allyFormation))
                                //{
                                //    float newDist = formation.QuerySystem.MedianPosition.AsVec2.Distance(allyFormation.QuerySystem.MedianPosition.AsVec2);
                                //    if (newDist < dist)
                                //    {
                                //        significantEnemy = allyFormation;
                                //        dist = newDist;
                                //    }
                                //}
                                //if (formation != null && includeHorseArchers && allyFormation.CountOfUnits > 0 && allyFormation.QuerySystem.IsRangedCavalryFormation)
                                //{
                                //    float newDist = formation.QuerySystem.MedianPosition.AsVec2.Distance(allyFormation.QuerySystem.MedianPosition.AsVec2);
                                //    if (newDist < dist)
                                //    {
                                //        significantEnemy = allyFormation;
                                //        dist = newDist;
                                //    }
                                //}
                            }
                        }
                    }
                }
            }
            return significantAlly;
        }

        public static float GetCombatAIDifficultyMultiplier()
        {
            MissionState missionState = Game.Current.GameStateManager.ActiveState as MissionState;
            if (missionState != null)
            {
                if (!RBMConfig.RBMConfig.vanillaCombatAi)
                {
                    if (missionState.MissionName.Equals("EnhancedBattleTestFieldBattle") || missionState.MissionName.Equals("EnhancedBattleTestSiegeBattle"))
                    {
                        return 1.0f;
                    }
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
                else
                {
                    switch (CampaignOptions.CombatAIDifficulty)
                    {
                        case CampaignOptions.Difficulty.VeryEasy:
                            return 0.1f;
                        case CampaignOptions.Difficulty.Easy:
                            return 0.32f;
                        case CampaignOptions.Difficulty.Realistic:
                            return 0.96f;
                        default:
                            return 0.5f;
                    }
                }
            }
            else
            {
                return 1f;
            }
        }

        public static float CalculateAILevel(Agent agent, int relevantSkillLevel)
        {
            float difficultyModifier = GetCombatAIDifficultyMultiplier();
            //float difficultyModifier = 1.0f; // v enhanced battle test je difficulty very easy
            return MBMath.ClampFloat((float)relevantSkillLevel / 250f * difficultyModifier, 0f, 1f);
        }

        //public static int GetMeleeSkill(Agent agent, WeaponComponentData equippedItem, WeaponComponentData secondaryItem)
        //{
        //    SkillObject skill = DefaultSkills.Athletics;
        //    if (equippedItem != null)
        //    {
        //        SkillObject relevantSkill = equippedItem.RelevantSkill;
        //        skill = ((relevantSkill == DefaultSkills.OneHanded || relevantSkill == DefaultSkills.Polearm) ? relevantSkill : ((relevantSkill != DefaultSkills.TwoHanded) ? DefaultSkills.OneHanded : ((secondaryItem == null) ? DefaultSkills.TwoHanded : DefaultSkills.OneHanded)));
        //    }
        //    return GetEffectiveSkill(agent.Character, agent.Origin, agent.Formation, skill);
        //}

        //public static int GetEffectiveSkill(BasicCharacterObject agentCharacter, IAgentOriginBase agentOrigin, Formation agentFormation, SkillObject skill)
        //{
        //    return agentCharacter.GetSkillValue(skill);
        //}

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

        public static bool CheckIfSkirmisherAgent(Agent agent, float ammoAmout = 0)
        {
            CharacterObject characterObject = agent.Character as CharacterObject;
            if (characterObject != null && characterObject.Tier > 3)
            {
                return false;
            }
            for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
            {
                if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                {
                    WeaponStatsData[] wsd = agent.Equipment[equipmentIndex].GetWeaponStatsData();
                    if (wsd[0].WeaponClass == (int)WeaponClass.Javelin && agent.Equipment[equipmentIndex].Amount > ammoAmout)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool CheckIfCanBrace(Agent agent)
        {
            for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
            {
                if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                {
                    MissionWeapon weapon = agent.Equipment[equipmentIndex];
                    if (weapon.IsEmpty)
                    {
                        return false;
                    }
                    foreach (WeaponComponentData weapon2 in weapon.Item.Weapons)
                    {
                        string weaponUsageId = weapon2.WeaponDescriptionId;
                        if (weaponUsageId != null && weaponUsageId.IndexOf("bracing", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }
            return false;
        }

        public static bool ShouldFormationCopyShieldWall(Formation formation, float haveShieldThreshold = 0.6f)
        {
            int countAll = 0;
            int countHasShield = 0;

            if(formation.Team.HasTeamAi)
            {
                FieldInfo field = typeof(TeamAIComponent).GetField("_currentTactic", BindingFlags.NonPublic | BindingFlags.Instance);
                field.DeclaringType.GetField("_currentTactic");
                TacticComponent currentTactic = (TacticComponent)field.GetValue(formation.Team.TeamAI);

                if(currentTactic != null && currentTactic.GetType() == typeof(RBMTacticAttackSplitInfantry) || currentTactic.GetType() == typeof(RBMTacticAttackSplitInfantry))
                {
                    return false;
                }
            }
            formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
            {
                if (agent != null)
                {
                    if (agent.HasShieldCached)
                    {
                        countHasShield++;
                    }
                    countAll++;
                }
            });

            if(countHasShield/countAll >= haveShieldThreshold)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static int CountSoldiersInPolygon(Formation formation, Vec2[] polygon)
        {
            int result = 0;

            formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
            {
                if (IsPointInPolygon(polygon, agent.Position.AsVec2))
                {
                    result++;
                }
            });

            return result;
        }

        public static bool IsPointInPolygon(Vec2[] polygon, Vec2 testPoint)
        {
            bool result = false;
            int j = polygon.Count() - 1;
            for (int i = 0; i < polygon.Count(); i++)
            {
                if (polygon[i].Y < testPoint.Y && polygon[j].Y >= testPoint.Y || polygon[j].Y < testPoint.Y && polygon[i].Y >= testPoint.Y)
                {
                    if (polygon[i].X + (testPoint.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) * (polygon[j].X - polygon[i].X) < testPoint.X)
                    {
                        result = !result;
                    }
                }
                j = i;
            }
            return result;
        }

        public static float GetPowerOfAgentsSum(IEnumerable<Agent> agents)
        {
            float result = 0f;
            foreach(Agent agent in agents)
            {
                result += agent.CharacterPowerCached;
            }
            return result;
        }

        public static string GetSiegeArcherPointsPath()
        {
            return BasePath.Name + "Modules/RBM/ModuleData/scene_positions/";
        }

    }
}

