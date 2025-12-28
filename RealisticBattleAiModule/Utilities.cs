using RBMConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ItemObject;

namespace RBMAI
{
    public static class Utilities
    {

        public static float swingSpeedTransfer = 4.5454545f;
        public static float thrustSpeedTransfer = 11.7647057f;
        public static bool HasBattleBeenJoined(Formation mainInfantry, bool hasBattleBeenJoined, float battleJoinRange = 75f)
        {
            bool isOnlyCavReamining = CheckIfOnlyCavRemaining(mainInfantry);
            if (isOnlyCavReamining)
            {
                return true;
            }
            if (mainInfantry != null)
            {
                if (FormationFightingInMelee(mainInfantry, 0.35f))
                {
                    return true;
                }
                if (mainInfantry != null && mainInfantry.CountOfUnits > 0)
                {
                    Formation enemyForamtion = RBMAI.Utilities.FindSignificantEnemy(mainInfantry, true, true, false, false, false, true);
                    if (enemyForamtion != null)
                    {
                        float distance = mainInfantry.CachedMedianPosition.AsVec2.Distance(enemyForamtion.CachedMedianPosition.AsVec2) + mainInfantry.Depth / 2f + enemyForamtion.Depth / 2f;
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
                            if (equipmentElement.Item.Effectiveness < 50f)
                            {
                                tier = (int)1;
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
                    MovementOrder movementOrder = formation.GetReadonlyMovementOrderReference();
                    if ((formation.QuerySystem.IsInfantryFormation || formation.QuerySystem.IsRangedFormation) && (movementOrder.OrderType == OrderType.ChargeWithTarget))
                    {
                        formations = RBMAI.Utilities.FindSignificantFormations(formation);
                        Formation priorityFormation = null;
                        if (movementOrder.OrderType == OrderType.ChargeWithTarget && movementOrder.TargetFormation != null && !formations.Contains(movementOrder.TargetFormation))
                        {
                            priorityFormation = movementOrder.TargetFormation;
                        }
                        if (formations.Count > 0 || priorityFormation != null)
                        {
                            return RBMAI.Utilities.NearestAgentFromMultipleFormations(agent.Position.AsVec2, formations, priorityFormation);
                        }
                    }
                    if (formation.QuerySystem.IsCavalryFormation && movementOrder.OrderType == OrderType.ChargeWithTarget)
                    {
                        formations = RBMAI.Utilities.FindSignificantFormations(formation);
                        Formation priorityFormation = null;
                        if (movementOrder.OrderType == OrderType.ChargeWithTarget && movementOrder.TargetFormation != null && !formations.Contains(movementOrder.TargetFormation))
                        {
                            priorityFormation = movementOrder.TargetFormation;
                        }
                        if (formations.Count > 0 || priorityFormation != null)
                        {
                            return RBMAI.Utilities.NearestAgentFromMultipleFormations(agent.Position.AsVec2, formations, priorityFormation);
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
            targetFormation?.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
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

        public static Agent NearestAgentFromMultipleFormations(Vec2 unitPosition, List<Formation> formations, Formation priorityFormation = null)
        {
            Agent targetAgent = null;
            float distance = 10000f;
            foreach (Formation formation in formations.ToList())
            {
                formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                {
                    if (agent.IsAIControlled)
                    {
                        if (!agent.IsRunningAway)
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
            if (priorityFormation != null && distance > 30f)
            {
                distance = 10000f;
                priorityFormation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
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
                    foreach (Formation enemyFormation in team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList())
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

        public static float RatioOfCrossbowmen(Formation formation)
        {
            float ratio = 0f;
            int crossCount = 0;
            if (formation != null)
            {
                formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                {
                    bool isCrossbowmen = false;
                    for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                    {
                        if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty)
                        {
                            if (agent.Equipment[equipmentIndex].Item.Type == ItemTypeEnum.Crossbow)
                            {
                                isCrossbowmen = true;
                                break;
                            }
                        }
                    }
                    if (isCrossbowmen)
                    {
                        crossCount++;
                    }
                });
                ratio = (float)crossCount / (float)formation.CountOfUnits;
                return ratio;
            }
            return ratio;
        }

        public static bool IsFormationShooting(Formation formation, float desiredRatio = 0.3f, float lastAttackTimeTreshold = 10f)
        {
            float ratio = 0f;
            int countOfShooting = 0;
            if (formation != null && Mission.Current != null)
            {
                float ratioOfCrossbowmen;
                if (RBMConfig.RBMConfig.rbmCombatEnabled)
                {
                    ratioOfCrossbowmen = RatioOfCrossbowmen(formation);
                }
                else
                {
                    ratioOfCrossbowmen = 0f;
                }
                formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                {
                    //float currentTime = agent.Mission.CurrentTime;
                    float currentTime = MBCommon.GetTotalMissionTime();
                    if (agent.LastRangedAttackTime > 0f && currentTime > agent.LastRangedAttackTime && (currentTime - agent.LastRangedAttackTime) < (lastAttackTimeTreshold + (20f * ratioOfCrossbowmen)))
                    {
                        countOfShooting++;
                    }
                    //else
                    //{
                    //    agent.ClearTargetFrame();
                    //}
                    ratio = (float)countOfShooting / (float)formation.CountOfUnits;
                });
                if (ratio > desiredRatio)
                {
                    return true;
                }
            }
            return false;
        }

        public static bool FormationActiveSkirmishersRatio(Formation formation, float desiredRatio)
        {
            float ratio = 0f;
            int countOfSkirmishers = 0;
            if (formation != null && Mission.Current != null)
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
                    //float currentTime = Mission.Current.CurrentTime;
                    float currentTime = MBCommon.GetTotalMissionTime();
                    if (agent.LastRangedAttackTime > 0f && currentTime - agent.LastRangedAttackTime < 6f && currentTime > agent.LastRangedAttackTime && ratio <= desiredRatio && ((float)countedUnits / (float)formation.CountOfUnits) <= desiredRatio)
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
            //float currentTime = Mission.Current.CurrentTime;
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
                    if ((currentTime - lastMeleeAttackTime < 6f) || (currentTime - lastMeleeHitTime < 6f))
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

        public static List<Formation> FindSignificantFormations(Formation formation, bool includeCavalry = false)
        {
            List<Formation> formations = new List<Formation>();
            foreach (Team team in Mission.Current.Teams.ToList())
            {
                if (team.IsEnemyOf(formation.Team))
                {
                    if (team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList().Count == 1)
                    {
                        formations.Add(team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList()[0]);
                        return formations;
                    }
                    foreach (Formation enemyFormation in team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList())
                    {
                        if (enemyFormation.QuerySystem.IsInfantryFormation)
                        {
                            formations.Add(enemyFormation);
                        }
                        if (enemyFormation.QuerySystem.IsRangedFormation)
                        {
                            formations.Add(enemyFormation);
                        }
                        if (includeCavalry && (enemyFormation.QuerySystem.IsCavalryFormation || enemyFormation.QuerySystem.IsRangedCavalryFormation))
                        {
                            formations.Add(enemyFormation);
                        }
                    }
                }
            }
            return formations;
        }

        public static List<Formation> FindSignificantArcherFormations(Formation formation)
        {
            List<Formation> formations = new List<Formation>();
            if (formation != null)
            {
                foreach (Team team in Mission.Current.Teams.ToList())
                {
                    if (team.IsEnemyOf(formation.Team))
                    {
                        if (team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList().Count == 1)
                        {
                            formations.Add(team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList()[0]);
                            return formations;
                        }
                        foreach (Formation enemyFormation in team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList())
                        {
                            if (formation != null && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsRangedFormation)
                            {
                                formations.Add(enemyFormation);
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
                foreach (Team team in Mission.Current.Teams.ToList())
                {
                    if (team.IsEnemyOf(formation.Team))
                    {
                        foreach (Formation enemyFormation in team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList())
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
                        float newDist = position.AsVec2.Distance(enemyFormation.CachedMedianPosition.AsVec2);
                        if (newDist < dist)
                        {
                            significantEnemy = enemyFormation;
                            dist = newDist;
                        }

                        float newUnitCountRatio = ((float)enemyFormation.CountOfUnits * unitCountModifier) / (float)formation.CountOfUnits;
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
                        float newDist = position.AsVec2.Distance(enemyFormation.CachedMedianPosition.AsVec2);
                        if (newDist < dist)
                        {
                            significantEnemy = enemyFormation;
                            dist = newDist;
                        }

                        float newUnitCountRatio = ((float)enemyFormation.CountOfUnits * unitCountModifier) / (float)formation.CountOfUnits;
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
                    //    float newDist = formation.CachedMedianPosition.AsVec2.Distance(enemyFormation.CachedMedianPosition.AsVec2);
                    //    if (newDist < dist)
                    //    {
                    //        significantEnemy = enemyFormation;
                    //        dist = newDist;
                    //    }
                    //}
                    //if (formation != null && includeMountedSkirmishers && enemyFormation.CountOfUnits > 0 && CheckIfMountedSkirmishFormation(enemyFormation))
                    //{
                    //    float newDist = formation.CachedMedianPosition.AsVec2.Distance(enemyFormation.CachedMedianPosition.AsVec2);
                    //    if (newDist < dist)
                    //    {
                    //        significantEnemy = enemyFormation;
                    //        dist = newDist;
                    //    }
                    //}
                    //if (formation != null && includeHorseArchers && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsRangedCavalryFormation)
                    //{
                    //    float newDist = formation.CachedMedianPosition.AsVec2.Distance(enemyFormation.CachedMedianPosition.AsVec2);
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
                            float newDist = position.AsVec2.Distance(significantFormation.CachedMedianPosition.AsVec2);
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
                            float newDist = position.AsVec2.Distance(significantFormation.CachedMedianPosition.AsVec2);
                            if (newDist < dist)
                            {
                                significantEnemy = significantFormation;
                                dist = newDist;
                            }
                        }
                    }
                }
                if (significantEnemy == null)
                {
                    dist = 10000f;
                    float unitCountRatio = 0f;
                    foreach (Formation enemyFormation in allEnemyFormations)
                    {
                        float newUnitCountRatio = (float)(enemyFormation.CountOfUnits) / (float)formation.CountOfUnits;
                        float newDist = formation.CachedMedianPosition.AsVec2.Distance(enemyFormation.CachedMedianPosition.AsVec2);
                        if (newDist < dist * newUnitCountRatio * 1.5f)
                        {
                            significantEnemy = enemyFormation;
                            unitCountRatio = newUnitCountRatio;
                            dist = newDist;
                        }
                    }
                }
            }

            return significantEnemy;
        }

        public static Formation FindSignificantEnemy(Formation formation, bool includeInfantry, bool includeRanged, bool includeCavalry, bool includeMountedSkirmishers, bool includeHorseArchers, bool unitCountMatters = true)
        {
            unitCountMatters = true;
            Formation significantEnemy = null;
            List<Formation> significantFormations = new List<Formation>();
            float dist = 10000f;
            float significantTreshold = 0.6f;
            List<Formation> allEnemyFormations = new List<Formation>();

            if (formation != null)
            {
                foreach (Team team in Mission.Current.Teams.ToList())
                {
                    if (team.IsEnemyOf(formation.Team))
                    {
                        foreach (Formation enemyFormation in team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList())
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
                        //float newDist = formation.CachedMedianPosition.AsVec2.Distance(enemyFormation.CachedMedianPosition.AsVec2);
                        //if (newDist < dist)
                        //{
                        //    significantEnemy = enemyFormation;
                        //    dist = newDist;
                        //}

                        if (unitCountMatters)
                        {
                            significantFormations.Add(enemyFormation);
                        }
                    }
                    if (formation != null && includeRanged && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsRangedFormation)
                    {
                        //float newDist = formation.CachedMedianPosition.AsVec2.Distance(enemyFormation.CachedMedianPosition.AsVec2);
                        //if (newDist < dist)
                        //{
                        //    significantEnemy = enemyFormation;
                        //    dist = newDist;
                        //}

                        if (unitCountMatters)
                        {
                            significantFormations.Add(enemyFormation);
                        }
                    }
                    if (formation != null && includeCavalry && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsCavalryFormation && !enemyFormation.QuerySystem.IsRangedCavalryFormation)
                    {
                        //float newDist = formation.CachedMedianPosition.AsVec2.Distance(enemyFormation.CachedMedianPosition.AsVec2);
                        //if (newDist < dist)
                        //{
                        //    significantEnemy = enemyFormation;
                        //    dist = newDist;
                        //}

                        if (unitCountMatters)
                        {
                            significantFormations.Add(enemyFormation);
                        }
                    }
                    //if (formation != null && includeMountedSkirmishers && enemyFormation.CountOfUnits > 0 && CheckIfMountedSkirmishFormation(enemyFormation))
                    //{
                    //    float newDist = formation.CachedMedianPosition.AsVec2.Distance(enemyFormation.CachedMedianPosition.AsVec2);
                    //    if (newDist < dist)
                    //    {
                    //        significantEnemy = enemyFormation;
                    //        dist = newDist;
                    //    }
                    //}
                    if (formation != null && includeHorseArchers && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsRangedCavalryFormation)
                    {
                        //float newDist = formation.CachedMedianPosition.AsVec2.Distance(enemyFormation.CachedMedianPosition.AsVec2);
                        //if (newDist < dist)
                        //{
                        //    significantEnemy = enemyFormation;
                        //    dist = newDist;
                        //}

                        if (unitCountMatters)
                        {
                            significantFormations.Add(enemyFormation);
                        }
                    }
                }

                if (unitCountMatters)
                {
                    if (significantFormations.Count > 0)
                    {
                        //float unitCount = 0;
                        float formationWeight = 10000f;
                        foreach (Formation significantFormation in significantFormations)
                        {
                            bool isMain = false;
                            if (significantFormation.AI != null)
                            {
                                isMain = significantFormation.AI.IsMainFormation;
                            }
                            float unitCount = (float)formation.CountOfUnits;
                            float distance = formation.CachedMedianPosition.AsVec2.Distance(significantFormation.CachedMedianPosition.AsVec2);
                            float newFormationWeight = (distance / unitCount) / (isMain ? 1.5f : 1f);

                            if (newFormationWeight < formationWeight)
                            {
                                significantEnemy = significantFormation;
                                formationWeight = newFormationWeight;
                            }
                        }
                    }
                    else
                    {
                        float unitCountRatio = 0f;
                        dist = 10000f;
                        foreach (Formation enemyFormation in allEnemyFormations)
                        {
                            float newUnitCountRatio = (float)(enemyFormation.CountOfUnits) / (float)formation.CountOfUnits;
                            float newDist = formation.CachedMedianPosition.AsVec2.Distance(enemyFormation.CachedMedianPosition.AsVec2);
                            if (formation != null && includeInfantry && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsInfantryFormation)
                            {
                                if (newDist < dist * newUnitCountRatio * 1.5f)
                                {
                                    significantEnemy = enemyFormation;
                                    unitCountRatio = newUnitCountRatio;
                                    dist = newDist;
                                }
                            }
                            if (formation != null && includeRanged && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsRangedFormation)
                            {
                                if (newDist < dist * newUnitCountRatio * 1.5f)
                                {
                                    significantEnemy = enemyFormation;
                                    dist = newDist;
                                }
                            }
                            if (formation != null && includeCavalry && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsCavalryFormation && !enemyFormation.QuerySystem.IsRangedCavalryFormation)
                            {
                                if (newDist < dist * newUnitCountRatio * 1.5f)
                                {
                                    significantEnemy = enemyFormation;
                                    dist = newDist;
                                }
                            }
                            if (formation != null && includeHorseArchers && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsRangedCavalryFormation)
                            {
                                if (newDist < dist * newUnitCountRatio * 1.5f)
                                {
                                    significantEnemy = enemyFormation;
                                    dist = newDist;
                                }
                            }
                        }
                    }
                    if (significantEnemy == null)
                    {
                        dist = 10000f;
                        float unitCountRatio = 0f;
                        foreach (Formation enemyFormation in allEnemyFormations)
                        {
                            float newUnitCountRatio = (float)(enemyFormation.CountOfUnits) / (float)formation.CountOfUnits;
                            float newDist = formation.CachedMedianPosition.AsVec2.Distance(enemyFormation.CachedMedianPosition.AsVec2);
                            if (newDist < dist * newUnitCountRatio * 1.5f)
                            {
                                significantEnemy = enemyFormation;
                                unitCountRatio = newUnitCountRatio;
                                dist = newDist;
                            }
                        }
                    }
                }
            }
            return significantEnemy;
        }

        [HandleProcessCorruptedStateExceptions]
        public static bool CheckIfOnlyCavRemaining(Formation formation)
        {
            List<Formation> allEnemyFormations = new List<Formation>();
            bool result = true;
            try
            {
                if (formation != null)
                {
                    foreach (Team team in Mission.Current.Teams.ToList())
                    {
                        if (team.IsEnemyOf(formation.Team))
                        {
                            foreach (Formation enemyFormation in team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList())
                            {
                                allEnemyFormations.Add(enemyFormation);
                            }
                        }
                    }

                    foreach (Formation enemyFormation in allEnemyFormations.ToList())
                    {
                        if (!enemyFormation.QuerySystem.IsCavalryFormation && !enemyFormation.QuerySystem.IsRangedCavalryFormation)
                        {
                            result = false;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                result = false;
            }
            return result;
        }

        public static Formation FindSignificantAlly(Formation formation, bool includeInfantry, bool includeRanged, bool includeCavalry, bool includeMountedSkirmishers, bool includeHorseArchers, bool unitCountMatters = false)
        {
            Formation significantAlly = null;
            float dist = 10000f;
            List<Formation> significantFormations = new List<Formation>();
            if (formation != null)
            {
                foreach (Team team in Mission.Current.Teams.ToList())
                {
                    if (!team.IsEnemyOf(formation.Team))
                    {
                        if (team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList().Count == 1)
                        {
                            significantAlly = team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList()[0];
                            return significantAlly;
                        }
                        if (unitCountMatters)
                        {
                            int unitCount = -1;
                            foreach (Formation allyFormation in team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList())
                            {
                                if (formation != null && includeInfantry && allyFormation.CountOfUnits > 0 && allyFormation.QuerySystem.IsInfantryFormation)
                                {
                                    if (allyFormation.CountOfUnits > unitCount)
                                    {
                                        significantAlly = allyFormation;
                                        unitCount = allyFormation.CountOfUnits;
                                    }
                                }
                                if (formation != null && includeRanged && allyFormation.CountOfUnits > 0 && allyFormation.QuerySystem.IsRangedFormation)
                                {
                                    if (allyFormation.CountOfUnits > unitCount)
                                    {
                                        significantAlly = allyFormation;
                                        unitCount = allyFormation.CountOfUnits;
                                    }
                                }
                                if (formation != null && includeCavalry && allyFormation.CountOfUnits > 0 && allyFormation.QuerySystem.IsCavalryFormation && !allyFormation.QuerySystem.IsRangedCavalryFormation)
                                {
                                    if (allyFormation.CountOfUnits > unitCount)
                                    {
                                        significantAlly = allyFormation;
                                        unitCount = allyFormation.CountOfUnits;
                                    }
                                }
                            }
                        }
                        else
                        {
                            foreach (Formation allyFormation in team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList())
                            {
                                if (formation != null && includeInfantry && allyFormation.CountOfUnits > 0 && allyFormation.QuerySystem.IsInfantryFormation)
                                {
                                    float newDist = formation.CachedMedianPosition.AsVec2.Distance(allyFormation.CachedMedianPosition.AsVec2);
                                    if (newDist < dist)
                                    {
                                        significantAlly = allyFormation;
                                        dist = newDist;
                                    }
                                }
                                if (formation != null && includeRanged && allyFormation.CountOfUnits > 0 && allyFormation.QuerySystem.IsRangedFormation)
                                {
                                    float newDist = formation.CachedMedianPosition.AsVec2.Distance(allyFormation.CachedMedianPosition.AsVec2);
                                    if (newDist < dist)
                                    {
                                        significantAlly = allyFormation;
                                        dist = newDist;
                                    }
                                }
                                if (formation != null && includeCavalry && allyFormation.CountOfUnits > 0 && allyFormation.QuerySystem.IsCavalryFormation && !allyFormation.QuerySystem.IsRangedCavalryFormation)
                                {
                                    float newDist = formation.CachedMedianPosition.AsVec2.Distance(allyFormation.CachedMedianPosition.AsVec2);
                                    if (newDist < dist)
                                    {
                                        significantAlly = allyFormation;
                                        dist = newDist;
                                    }
                                }
                                //if (formation != null && includeMountedSkirmishers && allyFormation.CountOfUnits > 0 && CheckIfMountedSkirmishFormation(allyFormation))
                                //{
                                //    float newDist = formation.CachedMedianPosition.AsVec2.Distance(allyFormation.CachedMedianPosition.AsVec2);
                                //    if (newDist < dist)
                                //    {
                                //        significantEnemy = allyFormation;
                                //        dist = newDist;
                                //    }
                                //}
                                //if (formation != null && includeHorseArchers && allyFormation.CountOfUnits > 0 && allyFormation.QuerySystem.IsRangedCavalryFormation)
                                //{
                                //    float newDist = formation.CachedMedianPosition.AsVec2.Distance(allyFormation.CachedMedianPosition.AsVec2);
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

            if (formation.Team.HasTeamAi)
            {
                FieldInfo field = typeof(TeamAIComponent).GetField("_currentTactic", BindingFlags.NonPublic | BindingFlags.Instance);
                field.DeclaringType.GetField("_currentTactic");
                TacticComponent currentTactic = (TacticComponent)field.GetValue(formation.Team.TeamAI);

                if (currentTactic != null && (currentTactic is RBMTacticAttackSplitInfantry || currentTactic is RBMTacticAttackSplitInfantry))
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

            if (countHasShield / countAll >= haveShieldThreshold)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static IEnumerable<Agent> CountSoldiersInPolygon(Formation formation, Vec2[] polygon)
        {
            List<Agent> enemyAgents = new List<Agent>();
            int result = 0;
            foreach (Team team in Mission.Current.Teams.ToList())
            {
                if (team.IsEnemyOf(formation.Team))
                {
                    foreach (Formation enemyFormation in team.FormationsIncludingEmpty.Where((Formation f) => f.CountOfUnits > 0).ToList())
                    {
                        formation.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
                        {
                            if (IsPointInPolygon(polygon, agent.Position.AsVec2))
                            {
                                result++;
                                enemyAgents.Add(agent);
                            }
                        });
                    }
                }
            }
            return enemyAgents;
        }

        public static bool IsPointInPolygon(Vec2[] polygon, Vec2 testPoint)
        {
            //bool result = false;
            //int j = polygon.Count() - 1;
            //for (int i = 0; i < polygon.Count(); i++)
            //{
            //    if (polygon[i].Y < testPoint.Y && polygon[j].Y >= testPoint.Y || polygon[j].Y < testPoint.Y && polygon[i].Y >= testPoint.Y)
            //    {
            //        if (polygon[i].X + (testPoint.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) * (polygon[j].X - polygon[i].X) < testPoint.X)
            //        {
            //            result = !result;
            //        }
            //    }
            //    j = i;
            //}
            //return result;
            Vec2 p1, p2;
            bool inside = false;

            if (polygon.Length < 3)
            {
                return inside;
            }

            var oldPoint = new Vec2(
                polygon[polygon.Length - 1].X, polygon[polygon.Length - 1].Y);

            for (int i = 0; i < polygon.Length; i++)
            {
                var newPoint = new Vec2(polygon[i].X, polygon[i].Y);

                if (newPoint.X > oldPoint.X)
                {
                    p1 = oldPoint;
                    p2 = newPoint;
                }
                else
                {
                    p1 = newPoint;
                    p2 = oldPoint;
                }

                if ((newPoint.X < testPoint.X) == (testPoint.X <= oldPoint.X)
                    && (testPoint.Y - (long)p1.Y) * (p2.X - p1.X)
                    < (p2.Y - (long)p1.Y) * (testPoint.X - p1.X))
                {
                    inside = !inside;
                }

                oldPoint = newPoint;
            }

            return inside;
        }

        public static float GetPowerOfAgentsSum(IEnumerable<Agent> agents)
        {
            float result = 0f;
            foreach (Agent agent in agents)
            {
                result += MBMath.ClampInt((int)Math.Floor(agent.CharacterPowerCached * 65), 75, 200);
            }
            return result;
        }

        public static string GetSiegeArcherPointsPath()
        {
            return BasePath.Name + "Modules/RBM/ModuleData/scene_positions/";
        }

        private static float GetPowerOriginal(int tier, bool isHero = false, bool isMounted = false)
        {
            return (float)((2 + tier) * (8 + tier)) * 0.02f * (isHero ? 1.5f : (isMounted ? 1.2f : 1f));
        }

        public static bool HitWithWeaponBladeTip(in AttackCollisionData collisionData, in MissionWeapon attackerWeapon)
        {
            WeaponComponentData currentUsageItem = attackerWeapon.CurrentUsageItem;
            if (currentUsageItem != null)
            {
                WeaponClass weaponClass = attackerWeapon.CurrentUsageItem.WeaponClass;
                if (collisionData.CollisionDistanceOnWeapon > currentUsageItem.GetRealWeaponLength() * 0.95f)
                {
                    return true;
                }
                return false;
            }
            return false;
        }

        public static bool HitWithWeaponBlade(in AttackCollisionData collisionData, in MissionWeapon attackerWeapon)
        {
            WeaponComponentData currentUsageItem = attackerWeapon.CurrentUsageItem;
            if (attackerWeapon.Item != null && currentUsageItem != null && attackerWeapon.Item.WeaponDesign != null &&
                attackerWeapon.Item.WeaponDesign.UsedPieces != null && attackerWeapon.Item.WeaponDesign.UsedPieces.Length > 0)
            {
                bool isSwordType = false;
                if (attackerWeapon.CurrentUsageItem != null)
                    switch (attackerWeapon.CurrentUsageItem.WeaponClass)
                    {
                        case WeaponClass.Dagger:
                        case WeaponClass.OneHandedSword:
                        case WeaponClass.TwoHandedSword:
                            {
                                isSwordType = true;
                                break;
                            }
                    }
                float bladeLength = attackerWeapon.Item.WeaponDesign.UsedPieces[0].ScaledBladeLength + (isSwordType ? 0f : 0.15f);
                float realWeaponLength = currentUsageItem.GetRealWeaponLength();
                if (collisionData.CollisionDistanceOnWeapon < (realWeaponLength - bladeLength))
                {
                    return false;
                }
                return true;
            }
            return true;
        }

        public static float GetComHitModifier(in AttackCollisionData collisionData, in MissionWeapon attackerWeapon)
        {
            WeaponComponentData currentUsageItem = attackerWeapon.CurrentUsageItem;
            if (collisionData.StrikeType == (int)StrikeType.Thrust)
            {
                if (collisionData.CollisionHitResultFlags == CombatHitResultFlags.NormalHit)
                {
                    return 1f;
                }
                else
                {
                    return 0.3f;
                }
            }

            float comHitModifier = 0f;
            if (attackerWeapon.Item != null && currentUsageItem != null && attackerWeapon.Item.WeaponDesign != null &&
                attackerWeapon.Item.WeaponDesign.UsedPieces != null && attackerWeapon.Item.WeaponDesign.UsedPieces.Length > 0)
            {
                float impactPointAsPercent = MBMath.ClampFloat(collisionData.CollisionDistanceOnWeapon, -0.2f, currentUsageItem.GetRealWeaponLength()) / currentUsageItem.GetRealWeaponLength();
                float comAsPercent = MBMath.ClampFloat(currentUsageItem.CenterOfMass, -0.2f, currentUsageItem.GetRealWeaponLength()) / currentUsageItem.GetRealWeaponLength();
                comHitModifier = 1f - Math.Abs(comAsPercent - impactPointAsPercent);
                if (attackerWeapon.CurrentUsageItem != null)
                {
                    switch (attackerWeapon.CurrentUsageItem.WeaponClass)
                    {
                        case WeaponClass.OneHandedAxe:
                        case WeaponClass.TwoHandedAxe:
                        case WeaponClass.Mace:
                        case WeaponClass.TwoHandedMace:
                        case WeaponClass.TwoHandedPolearm:
                            {
                                if (collisionData.StrikeType == (int)StrikeType.Swing)
                                {
                                    if (HitWithWeaponBlade(collisionData, attackerWeapon))
                                    {
                                        return 1f;
                                    }
                                    else
                                    {
                                        return 0.3f;
                                    }
                                }
                                break;
                            }
                        case WeaponClass.Dagger:
                        case WeaponClass.OneHandedSword:
                        case WeaponClass.TwoHandedSword:
                            {
                                float bladeLength = attackerWeapon.Item.WeaponDesign.UsedPieces[0].ScaledBladeLength + 0f;
                                float realWeaponLength = currentUsageItem.GetRealWeaponLength();
                                if (collisionData.CollisionDistanceOnWeapon < (realWeaponLength - bladeLength))
                                {
                                    return 1f;
                                }
                                break;
                            }
                    }
                }
                if (comHitModifier > 0.66f)
                {
                    return 1f;
                }
                else if (comHitModifier > 0.33f)
                {
                    return 0.66f;
                }
                else
                {
                    return 0.33f;
                }
            }
            return comHitModifier;
        }

        public static float CalculateSkillModifier(int relevantSkillLevel)
        {
            return MBMath.ClampFloat((float)relevantSkillLevel / 250f, 0f, 1f);
        }

        public static float CalculateSkillModifier(float relevantSkillLevel)
        {
            return MBMath.ClampFloat(relevantSkillLevel / 250f, 0f, 1f);
        }

        public static float GetEffectiveSkillWithDR(int effectiveSkill)
        {
            float effectiveSkillWithDR = 0f;
            effectiveSkillWithDR = (600f / (600f + effectiveSkill)) * (float)effectiveSkill;

            //float oneskillStep = 25f;
            //int skillSteps = MathF.Floor(effectiveSkill / 25f);
            //for(int i = 1; i <= skillSteps; i++)
            //{
            //    effectiveSkillWithDR = MathF.Pow(i * oneskillStep, 1f - ((i-1)/100f));
            //}
            return effectiveSkillWithDR;
        }

        public const float oneHandedPolearmThrustStrength = 2.5f;
        public const float twoHandedPolearmThrustStrength = 5f;

        public static float CalculateThrustMagnitudeForOneHandedWeapon(float weaponWeight, float effectiveSkill, float thrustSpeed, float exraLinearSpeed, Agent.UsageDirection attackDirection)
        {
            float magnitude = 0f;

            bool isOverheadAttack = attackDirection == Agent.UsageDirection.AttackUp;

            thrustSpeed = (isOverheadAttack ? thrustSpeed * 1.33f : thrustSpeed);
            if (thrustSpeed > 9f)
            {
                thrustSpeed = 9f;
            }
            float combinedSpeed = thrustSpeed + exraLinearSpeed;
            float skillModifier = Utilities.CalculateSkillModifier(effectiveSkill) * 2f;

            float spearKineticEnergy = 0.5f * weaponWeight * (combinedSpeed * combinedSpeed);

            float armStrength = isOverheadAttack ? oneHandedPolearmThrustStrength - 1f : oneHandedPolearmThrustStrength;

            float thrustStrength = weaponWeight + (armStrength * (1f + skillModifier));
            float thrustStrengthWithWeaponWeight = weaponWeight + (armStrength * (1f + skillModifier));

            float thrustEnergyCap = MathF.Clamp(0.5f * thrustStrength * (thrustSpeed * thrustSpeed) * 1.5f, 0f, 180f);
            float thrustEnergy = 0.5f * thrustStrengthWithWeaponWeight * (combinedSpeed * combinedSpeed);
            if (thrustEnergy > thrustEnergyCap)
            {
                thrustEnergy = thrustEnergyCap;
            }

            magnitude = thrustEnergy;

            if (spearKineticEnergy > magnitude)
            {
                magnitude = spearKineticEnergy;
            }

            if (magnitude > thrustEnergyCap)
            {
                magnitude = thrustEnergyCap;
            }

            return magnitude * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
        }

        public static float CalculateThrustMagnitudeForTwoHandedWeapon(float weaponWeight, float effectiveSkill, float thrustSpeed, float exraLinearSpeed, Agent.UsageDirection attackDirection)
        {
            float magnitude = 0f;

            bool isOverheadAttack = attackDirection == Agent.UsageDirection.AttackUp;
            thrustSpeed = (isOverheadAttack ? thrustSpeed + 1f : thrustSpeed);
            if (thrustSpeed > 6f)
            {
                thrustSpeed = 6f;
            }
            float combinedSpeed = thrustSpeed + exraLinearSpeed;
            float skillModifier = Utilities.CalculateSkillModifier(effectiveSkill) * 2f;

            float spearKineticEnergy = 0.5f * weaponWeight * (combinedSpeed * combinedSpeed);

            float armStrength = isOverheadAttack ? twoHandedPolearmThrustStrength - 1f : twoHandedPolearmThrustStrength;

            float thrustStrength = armStrength * (1f + skillModifier);
            float thrustStrengthWithWeaponWeight = weaponWeight + (armStrength * (1f + skillModifier));

            float thrustEnergyCap = MathF.Clamp(0.5f * thrustStrength * (thrustSpeed * thrustSpeed) * 1.5f, 0f, 250f);

            float thrustEnergy = 0.5f * thrustStrengthWithWeaponWeight * (combinedSpeed * combinedSpeed);
            if (thrustEnergy > thrustEnergyCap)
            {
                thrustEnergy = thrustEnergyCap;
            }

            magnitude = thrustEnergy;

            if (spearKineticEnergy > magnitude)
            {
                magnitude = spearKineticEnergy;
            }

            if (magnitude > thrustEnergyCap)
            {
                magnitude = thrustEnergyCap;
            }

            return magnitude * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
        }

        private static float WeaponTypeDamage(RBMCombatConfigWeaponType weaponTypeFactors, float magnitude, float armorReduction, DamageTypes damageType, float armorEffectiveness, BasicCharacterObject player, bool isPlayerVictim, float weaponDamageFactor, out float penetratedDamage, out float bluntTraumaAfterArmor, float partialPenetrationThreshold = 2f)
        {
            float damage = 0f;
            float armorThresholdModifier = RBMConfig.RBMConfig.armorThresholdModifier / weaponDamageFactor;
            switch (damageType)
            {
                case DamageTypes.Blunt:
                    {
                        //float armorReductionBlunt = 100f / ((100f + armorEffectiveness) * RBMConfig.RBMConfig.dict["Global.ArmorMultiplier"]);
                        //damage += magnitude * armorReductionBlunt * RBMConfig.RBMConfig.dict["Global.MaceBluntModifier"];

                        penetratedDamage = Math.Max(0f, magnitude - armorEffectiveness * 5f * armorThresholdModifier);
                        float bluntFraction = 0f;
                        if (magnitude > 0f)
                        {
                            bluntFraction = (magnitude - penetratedDamage) / magnitude;
                        }
                        damage += penetratedDamage;

                        float bluntTrauma = magnitude * (0.5f * RBMConfig.RBMConfig.maceBluntModifier) * bluntFraction;
                        bluntTraumaAfterArmor = Math.Max(0f, bluntTrauma * armorReduction);
                        damage += bluntTraumaAfterArmor;

                        break;
                    }
                case DamageTypes.Cut:
                    {
                        penetratedDamage = Math.Max(0f, magnitude - armorEffectiveness * weaponTypeFactors.ExtraArmorThresholdFactorCut * armorThresholdModifier);
                        float bluntFraction = 0f;
                        if (magnitude > 0f)
                        {
                            bluntFraction = (magnitude - penetratedDamage) / magnitude;
                        }
                        damage += penetratedDamage;

                        float bluntTrauma = magnitude * (weaponTypeFactors.ExtraBluntFactorCut + RBMConfig.RBMConfig.bluntTraumaBonus) * bluntFraction;
                        bluntTraumaAfterArmor = Math.Max(0f, bluntTrauma * armorReduction);
                        damage += bluntTraumaAfterArmor;

                        if (RBMConfig.RBMConfig.armorPenetrationMessage)
                        {
                            MBTextManager.SetTextVariable("DMG1", (int)(bluntTraumaAfterArmor));
                            MBTextManager.SetTextVariable("DMG2", (int)(penetratedDamage));
                            if (player != null)
                            {
                                if (isPlayerVictim)
                                {
                                    //InformationManager.DisplayMessage(new InformationMessage("You received"));
                                    InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_021}You received {DMG1} blunt trauma, {DMG2} armor penetration damage").ToString()));
                                    //InformationManager.DisplayMessage(new InformationMessage("damage penetrated: " + penetratedDamage));
                                }
                                else
                                {
                                    InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_022}You dealt {DMG1} blunt trauma, {DMG2} armor penetration damage").ToString()));
                                }
                            }
                        }
                        break;
                    }
                case DamageTypes.Pierce:
                    {
                        float partialPenetration = Math.Max(0f, magnitude - armorEffectiveness * partialPenetrationThreshold * armorThresholdModifier);
                        if (partialPenetration > 15f)
                        {
                            partialPenetration = 15f;
                        }
                        penetratedDamage = Math.Max(0f, magnitude - armorEffectiveness * weaponTypeFactors.ExtraArmorThresholdFactorPierce * armorThresholdModifier) - partialPenetration;
                        float bluntFraction = 0f;
                        if (magnitude > 0f)
                        {
                            bluntFraction = (magnitude - (penetratedDamage + partialPenetration)) / magnitude;
                        }
                        penetratedDamage += partialPenetration;
                        damage += penetratedDamage;

                        float bluntTrauma = magnitude * (weaponTypeFactors.ExtraBluntFactorPierce + RBMConfig.RBMConfig.bluntTraumaBonus) * bluntFraction;
                        bluntTraumaAfterArmor = Math.Max(0f, bluntTrauma * armorReduction);
                        damage += bluntTraumaAfterArmor;

                        if (RBMConfig.RBMConfig.armorPenetrationMessage)
                        {
                            MBTextManager.SetTextVariable("DMG1", (int)(bluntTraumaAfterArmor));
                            MBTextManager.SetTextVariable("DMG2", (int)(penetratedDamage));
                            if (player != null)
                            {
                                if (isPlayerVictim)
                                {
                                    //InformationManager.DisplayMessage(new InformationMessage("You received"));
                                    InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_021}You received {DMG1} blunt trauma, {DMG2} armor penetration damage").ToString()));
                                    //InformationManager.DisplayMessage(new InformationMessage("damage penetrated: " + penetratedDamage));
                                }
                                else
                                {
                                    InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=RBM_AI_022}You dealt {DMG1} blunt trauma, {DMG2} armor penetration damage").ToString()));
                                }
                            }
                        }
                        break;
                    }
                default:
                    {
                        penetratedDamage = 0f;
                        bluntTraumaAfterArmor = 0f;
                        damage = 0f;
                        break;
                    }
            }
            return damage;
        }


        public static float RBMComputeDamage(string weaponType, DamageTypes damageType, float magnitude, float armorEffectiveness, float absorbedDamageRatio, out float penetratedDamage, out float bluntTraumaAfterArmor, float weaponDamageFactor = 1f, BasicCharacterObject player = null, bool isPlayerVictim = false)
        {
            float damage = 0f;
            float armorReduction = 100f / (100f + armorEffectiveness * RBMConfig.RBMConfig.armorMultiplier);
            float mag_1h_thrust;
            float mag_2h_thrust;
            float mag_1h_sword_thrust;
            float mag_2h_sword_thrust;

            if (damageType == DamageTypes.Pierce)
            {
                mag_1h_thrust = magnitude * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                mag_2h_thrust = magnitude * 1f * RBMConfig.RBMConfig.TwoHandedThrustDamageBonus;
                mag_1h_sword_thrust = magnitude * 1.0f * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                mag_2h_sword_thrust = magnitude * 1f * RBMConfig.RBMConfig.TwoHandedThrustDamageBonus;
            }
            else if (damageType == DamageTypes.Cut)
            {
                mag_1h_thrust = magnitude;
                mag_2h_thrust = magnitude;
                mag_1h_sword_thrust = magnitude * 1.0f;
                mag_2h_sword_thrust = magnitude * 1.00f;
            }
            else
            {
                mag_1h_thrust = magnitude;
                mag_2h_thrust = magnitude;
                mag_1h_sword_thrust = magnitude;
                mag_2h_sword_thrust = magnitude;
            }

            switch (weaponType)
            {
                case "Dagger":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_sword_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "ThrowingKnife":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_sword_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "OneHandedSword":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_sword_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "TwoHandedSword":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_2h_sword_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "OneHandedAxe":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "OneHandedBastardAxe":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "TwoHandedAxe":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "OneHandedPolearm":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "TwoHandedPolearm":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_2h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "Mace":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor, 0f);
                        break;
                    }
                case "TwoHandedMace":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_2h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "Arrow":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor, 0f);
                        break;
                    }
                case "Bolt":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor, 0f);
                        break;
                    }
                case "Javelin":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "ThrowingAxe":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), mag_1h_thrust, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
                case "SlingStone":
                    {
                        damage = WeaponTypeDamage(RBMConfig.RBMConfig.getWeaponTypeFactors(weaponType), magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor, 0f);
                        break;
                    }
                default:
                    {
                        //InformationManager.DisplayMessage(new InformationMessage("POZOR DEFAULT !!!!"));
                        RBMCombatConfigWeaponType defaultwct = new RBMCombatConfigWeaponType("default", 1f, 1f, 1f, 1f, 1f, 1f);
                        damage = WeaponTypeDamage(defaultwct, magnitude, armorReduction, damageType, armorEffectiveness, player, isPlayerVictim, weaponDamageFactor, out penetratedDamage, out bluntTraumaAfterArmor);
                        break;
                    }
            }
            return damage * absorbedDamageRatio;
        }


        public static float GetSkillBasedDamage(float magnitude, bool isPassiveUsage, string weaponType, DamageTypes damageType, float effectiveSkill, float skillModifier, StrikeType strikeType, float weaponWeight)
        {
            float skillBasedDamage = 0f;
            const float ashBreakTreshold = 430f;
            const float poplarBreakTreshold = 260f;
            float BraceBonus = 0f;
            float BraceModifier = 1f; // because lances have 3 times more damage
            switch (weaponType)
            {
                case "Dagger":
                case "OneHandedSword":
                case "ThrowingKnife":
                    {
                        if (damageType == DamageTypes.Cut)
                        {
                            float value = magnitude + (effectiveSkill * 0.133f);
                            float min = 5f * (1 + skillModifier);
                            float max = 15f * (1 + (2 * skillModifier));
                            skillBasedDamage = (MBMath.ClampFloat(value, min, max) * 4f);
                            //skillBasedDamage = magnitude + 40f + (effectiveSkill * 0.53f);
                        }
                        else if (damageType == DamageTypes.Blunt)
                        {
                            //skillBasedDamage = magnitude + 0.50f * (40f + (effectiveSkill * 0.53f));
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.075f), 15f * (1 + skillModifier), 20f * (1 + (2 * skillModifier))) * 4f) * 0.4f;
                        }
                        else
                        {
                            if (strikeType == (int)StrikeType.Swing)
                            {
                                skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.133f), 5f * (1 + skillModifier), 15f * (1 + (2 * skillModifier))) * 4f) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                            }
                            else
                            {
                                //float weaponWeight = attacker.Equipment[attacker.GetWieldedItemIndex(HandIndex.MainHand)].GetWeight();
                                //float totalSpeed = (float)Math.Sqrt((magnitude * 2) / 8f);
                                //totalSpeed += 3f;
                                //skillBasedDamage = 0.5f * 8f * totalSpeed * totalSpeed * (1 + (skillModifier * 0.4f));
                                //if (skillBasedDamage > 170f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier)
                                //{
                                //    skillBasedDamage = 170f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                //}
                                skillBasedDamage = magnitude;
                            }
                        }
                        if (magnitude > 1f)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
                case "TwoHandedSword":
                    {
                        if (damageType == DamageTypes.Cut)
                        {
                            float value = magnitude + (effectiveSkill * 0.173f);
                            float min = 12f * (1 + skillModifier);
                            float max = 20f * (1 + (2 * skillModifier));
                            skillBasedDamage = MBMath.ClampFloat(value, min, max) * 4f;
                        }
                        else if (damageType == DamageTypes.Blunt)
                        {
                            //skillBasedDamage = magnitude * 1.3f + 0.5f * ((40f + (effectiveSkill * 0.53f)) * 1.3f);
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.0975f), 20f * (1 + skillModifier), 26f * (1 + (2 * skillModifier))) * 4f) * 0.4f;
                        }
                        else
                        {
                            if (strikeType == (int)StrikeType.Swing)
                            {
                                skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.133f), 12f * (1 + skillModifier), 20f * (1 + (2 * skillModifier))) * 4f) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                            }
                            else
                            {
                                //float weaponWeight = attacker.Equipment[attacker.GetWieldedItemIndex(HandIndex.MainHand)].GetWeight();
                                //float totalSpeed = (float)Math.Sqrt((magnitude * 2) / 8f);
                                //skillBasedDamage = 0.5f * 15f * totalSpeed * totalSpeed * (1 + (skillModifier * 0.4f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                //if (skillBasedDamage > 240f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier)
                                //{
                                //    skillBasedDamage = 240 * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                //}
                                skillBasedDamage = magnitude;
                            }
                        }
                        if (magnitude > 1f)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
                case "OneHandedAxe":
                case "ThrowingAxe":
                    {
                        float value = magnitude + (effectiveSkill * 0.1f);
                        float min = 10f * (1 + skillModifier);
                        float max = 18f * (1 + (2 * skillModifier));
                        skillBasedDamage = (MBMath.ClampFloat(value, min, max) * 4f);
                        if (damageType == DamageTypes.Blunt)
                        {
                            //skillBasedDamage = magnitude + 0.5f * (60f + (effectiveSkill * 0.4f));
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.075f), 15f * (1 + skillModifier), 20f * (1 + (2 * skillModifier))) * 4f) * 0.3f;
                        }
                        if (magnitude > 1f)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
                case "OneHandedBastardAxe":
                    {
                        skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.115f), 12f * (1 + skillModifier), 20f * (1 + (2 * skillModifier))) * 4f);
                        if (damageType == DamageTypes.Blunt)
                        {
                            //skillBasedDamage = magnitude * 1.15f + 0.5f * ((60f + (effectiveSkill * 0.4f)) * 1.15f);
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.08625f), 20f * (1 + skillModifier), 26f * (1 + (2 * skillModifier))) * 4f) * 0.3f;
                        }
                        if (magnitude > 1f)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
                case "TwoHandedAxe":
                    {
                        float value = magnitude + (effectiveSkill * 0.13f);
                        float min = 15f * (1 + skillModifier);
                        float max = 24f * (1 + (2 * skillModifier));
                        skillBasedDamage = (MBMath.ClampFloat(value, min, max) * 4f);
                        if (damageType == DamageTypes.Blunt)
                        {
                            //skillBasedDamage = magnitude * 1.3f + 0.5f * ((60f + (effectiveSkill * 0.4f)) * 1.30f);
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.0975f), 20f * (1 + skillModifier), 26f * (1 + (2 * skillModifier))) * 4f) * 0.3f;
                        }
                        if (magnitude > 1f)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
                case "Mace":
                    {
                        if (damageType == DamageTypes.Pierce)
                        {
                            //float totalSpeed = (float)Math.Sqrt((magnitude * 2) / 8f);
                            //totalSpeed += 3f;
                            //skillBasedDamage = 0.5f * 8f * totalSpeed * totalSpeed * (1 + (skillModifier * 0.4f));

                            //if (skillBasedDamage > 170f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier)
                            //{
                            //    skillBasedDamage = 170f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                            //}
                            skillBasedDamage = magnitude;
                        }
                        else
                        {
                            float value = magnitude + (effectiveSkill * 0.075f);
                            float min = 10f * (1 + skillModifier);
                            float max = 15f * (1 + (2 * skillModifier));
                            skillBasedDamage = (MBMath.ClampFloat(value, min, max) * 4f);
                            //skillBasedDamage = value;
                        }
                        if (magnitude > 1f)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
                case "TwoHandedMace":
                    {
                        if (damageType == DamageTypes.Pierce)
                        {
                            skillBasedDamage = (magnitude * 0.2f + 40f * RBMConfig.RBMConfig.ThrustMagnitudeModifier + (effectiveSkill * 0.4f * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) * 1.3f;
                        }
                        else
                        {
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.0975f), 20f * (1 + skillModifier), 26f * (1 + (2 * skillModifier))) * 4f);
                        }
                        if (magnitude > 1f)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
                case "OneHandedPolearm":
                    {
                        if (damageType == DamageTypes.Cut)
                        {
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.1f), 15f * (1 + skillModifier), 24f * (1 + (2 * skillModifier))) * 4f);
                        }
                        else if (damageType == DamageTypes.Blunt && !isPassiveUsage)
                        {
                            //skillBasedDamage = magnitude + 30f + (effectiveSkill * 0.26f);
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.075f), 15f * (1 + skillModifier), 20f * (1 + (2 * skillModifier))) * 4f) * 0.3f;
                        }
                        else
                        {
                            if (isPassiveUsage)
                            {
                                float couchedSkill = 0.5f + effectiveSkill * 0.02f;
                                float skillCap = (150f + effectiveSkill * 1.5f);

                                if (weaponWeight < 2.1f)
                                {
                                    BraceBonus += 0.5f;
                                    BraceModifier *= 1f;
                                }
                                float lanceBalistics = (magnitude * BraceModifier) / weaponWeight;
                                float CouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus);
                                float BluntLanceBalistics = ((magnitude * BraceModifier) / weaponWeight) * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                                float BluntCouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus) * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                                magnitude = CouchedMagnitude;

                                if (damageType == DamageTypes.Blunt)
                                {
                                    magnitude = BluntCouchedMagnitude;
                                    if (BluntCouchedMagnitude > skillCap && (BluntLanceBalistics * (weaponWeight + BraceBonus)) < skillCap) //skill based damage
                                    {
                                        magnitude = skillCap;
                                    }

                                    if ((BluntLanceBalistics * (weaponWeight + BraceBonus)) >= skillCap) //ballistics
                                    {
                                        magnitude = (BluntLanceBalistics * (weaponWeight + BraceBonus));
                                    }

                                    if (magnitude > poplarBreakTreshold) // damage cap - lance break threshold
                                    {
                                        magnitude = poplarBreakTreshold;
                                    }
                                    magnitude *= 1f;
                                }
                                else
                                {
                                    if (CouchedMagnitude > (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier) && (lanceBalistics * (weaponWeight + BraceBonus)) < (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) //skill based damage
                                    {
                                        magnitude = skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                    }

                                    if ((lanceBalistics * (weaponWeight + BraceBonus)) >= (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) //ballistics
                                    {
                                        magnitude = (lanceBalistics * (weaponWeight + BraceBonus));
                                    }

                                    if (magnitude > (ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) // damage cap - lance break threshold
                                    {
                                        magnitude = ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                    }
                                }
                            }
                            else
                            {
                                float totalSpeed = (float)Math.Sqrt((magnitude * 2f) / 8f);
                                //totalSpeed += 3f;
                                skillBasedDamage = magnitude;

                                //skillBasedDamage = magnitude * 0.4f + 60f * RBMConfig.RBMConfig.ThrustMagnitudeModifier + (effectiveSkill * 0.26f * RBMConfig.RBMConfig.ThrustMagnitudeModifier);
                                //if (skillBasedDamage > 170f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier)

                                //{
                                //    skillBasedDamage = 170f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                //}
                            }
                        }
                        if (magnitude > 0.15f && !isPassiveUsage)
                        {
                            magnitude = skillBasedDamage;
                        }
                        //else if(magnitude > 0f && magnitude <= 0.15f)
                        //{
                        //    InformationManager.DisplayMessage(new InformationMessage("DEBUG WARNING: strike bagnitude below treshlod"));
                        //}
                        break;
                    }
                case "TwoHandedPolearm":
                    {
                        if (damageType == DamageTypes.Cut)
                        {
                            float value = magnitude + (effectiveSkill * 0.1495f);
                            float min = 18f * (1 + skillModifier);
                            float max = 28f * (1 + (2 * skillModifier));
                            skillBasedDamage = (MBMath.ClampFloat(value, min, max) * 4f);
                        }
                        else if (damageType == DamageTypes.Blunt && !isPassiveUsage)
                        {
                            //skillBasedDamage = magnitude + (30f + (effectiveSkill * 0.26f) * 1.3f);
                            skillBasedDamage = (MBMath.ClampFloat(magnitude + (effectiveSkill * 0.0975f), 20f * (1 + skillModifier), 26f * (1 + (2 * skillModifier))) * 4f) * 0.3f;
                        }
                        else
                        {
                            if (isPassiveUsage)
                            {
                                float couchedSkill = 0.5f + effectiveSkill * 0.02f;
                                float skillCap = (150f + effectiveSkill * 1.5f);

                                if (weaponWeight < 2.1f)
                                {
                                    BraceBonus += 0.5f;
                                    BraceModifier *= 1f;
                                }
                                float lanceBalistics = (magnitude * BraceModifier) / weaponWeight;
                                float CouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus);
                                float BluntLanceBalistics = ((magnitude * BraceModifier) / weaponWeight) * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                                float BluntCouchedMagnitude = lanceBalistics * (weaponWeight + couchedSkill + BraceBonus) * RBMConfig.RBMConfig.OneHandedThrustDamageBonus;
                                magnitude = CouchedMagnitude;

                                if (damageType == DamageTypes.Blunt)
                                {
                                    magnitude = BluntCouchedMagnitude;
                                    if (BluntCouchedMagnitude > skillCap && (BluntLanceBalistics * (weaponWeight + BraceBonus)) < skillCap) //skill based damage
                                    {
                                        magnitude = skillCap;
                                    }

                                    if ((BluntLanceBalistics * (weaponWeight + BraceBonus)) >= skillCap) //ballistics
                                    {
                                        magnitude = (BluntLanceBalistics * (weaponWeight + BraceBonus));
                                    }

                                    if (magnitude > poplarBreakTreshold) // damage cap - lance break threshold
                                    {
                                        magnitude = poplarBreakTreshold;
                                    }
                                    magnitude *= 1f;
                                }
                                else
                                {
                                    if (CouchedMagnitude > (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier) && (lanceBalistics * (weaponWeight + BraceBonus)) < (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) //skill based damage
                                    {
                                        magnitude = skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                    }

                                    if ((lanceBalistics * (weaponWeight + BraceBonus)) >= (skillCap * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) //ballistics
                                    {
                                        magnitude = (lanceBalistics * (weaponWeight + BraceBonus));
                                    }

                                    if (magnitude > (ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) // damage cap - lance break threshold
                                    {
                                        magnitude = ashBreakTreshold * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                    }
                                }
                            }
                            else
                            {
                                //float weaponWeight = attacker.Equipment[attacker.GetWieldedItemIndex(HandIndex.MainHand)].GetWeight();
                                //float totalSpeed = (float)Math.Sqrt((magnitude * 2f) / 8f);
                                //skillBasedDamage = 0.5f * 15f * totalSpeed * totalSpeed * (1 + (skillModifier * 0.4f));
                                ////skillBasedDamage = (magnitude * 0.4f + 60f * RBMConfig.RBMConfig.ThrustMagnitudeModifier + (effectiveSkill * 0.26f * RBMConfig.RBMConfig.ThrustMagnitudeModifier)) * 1.3f;

                                //if (skillBasedDamage > 240f * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier)
                                //{
                                //    skillBasedDamage = 240 * (1 + (skillModifier * 0.5f)) * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                                //}
                                skillBasedDamage = magnitude;
                            }
                        }
                        if (magnitude > 0.15f && !isPassiveUsage)
                        {
                            magnitude = skillBasedDamage;
                        }
                        break;
                    }
            }
            return magnitude;
        }

        public static void CalculateVisualSpeeds(EquipmentElement weapon, int weaponUsageIndex, float effectiveSkillDR, out int swingSpeedReal, out int thrustSpeedReal, out int handlingReal)
        {
            swingSpeedReal = -1;
            thrustSpeedReal = -1;
            handlingReal = -1;
            if (!weapon.IsEmpty && weapon.Item != null && weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex) != null)
            {
                int swingSpeed = weapon.GetModifiedSwingSpeedForUsage(weaponUsageIndex);
                int handling = weapon.GetModifiedHandlingForUsage(weaponUsageIndex);

                switch (weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).WeaponClass)
                {
                    case WeaponClass.LowGripPolearm:
                    case WeaponClass.Mace:
                    case WeaponClass.OneHandedAxe:
                    case WeaponClass.OneHandedPolearm:
                    case WeaponClass.TwoHandedMace:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.83f) * swingskillModifier);
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.Weight, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).TotalInertia, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass) * Utilities.thrustSpeedTransfer);
                            thrustSpeedReal = MathF.Ceiling((thrustSpeedReal * 1.1f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 0.83f) * handlingskillModifier);
                            break;
                        }
                    case WeaponClass.TwoHandedPolearm:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.83f) * swingskillModifier);
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.Weight, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).TotalInertia, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass) * Utilities.thrustSpeedTransfer);
                            thrustSpeedReal = MathF.Ceiling((thrustSpeedReal * 1.05f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 5f) * handlingskillModifier);
                            break;
                        }
                    case WeaponClass.TwoHandedAxe:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 800f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.75f) * swingskillModifier);
                            thrustSpeedReal = MathF.Ceiling((weapon.GetModifiedThrustSpeedForUsage(weaponUsageIndex) * 0.9f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 0.83f) * handlingskillModifier);
                            break;
                        }
                    case WeaponClass.OneHandedSword:
                    case WeaponClass.Dagger:
                    case WeaponClass.TwoHandedSword:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 800f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 800f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 800f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.83f) * swingskillModifier);
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.Weight, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).TotalInertia, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass) * Utilities.thrustSpeedTransfer);
                            thrustSpeedReal = MathF.Ceiling((thrustSpeedReal * 1.15f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 0.9f) * handlingskillModifier);
                            break;
                        }
                }
            }
        }

        public static void CalculateVisualSpeeds(MissionWeapon weapon, int weaponUsageIndex, float effectiveSkillDR, out int swingSpeedReal, out int thrustSpeedReal, out int handlingReal)
        {
            swingSpeedReal = -1;
            thrustSpeedReal = -1;
            handlingReal = -1;
            if (!weapon.IsEmpty && weapon.Item != null && weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex) != null)
            {
                int swingSpeed = weapon.GetModifiedSwingSpeedForCurrentUsage();
                int handling = weapon.GetModifiedHandlingForCurrentUsage();

                switch (weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).WeaponClass)
                {
                    case WeaponClass.LowGripPolearm:
                    case WeaponClass.Mace:
                    case WeaponClass.OneHandedAxe:
                    case WeaponClass.OneHandedPolearm:
                    case WeaponClass.TwoHandedMace:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.83f) * swingskillModifier);
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.GetWeight(), weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).TotalInertia, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass) * Utilities.thrustSpeedTransfer);
                            thrustSpeedReal = MathF.Ceiling((thrustSpeedReal * 1.1f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 0.83f) * handlingskillModifier);
                            break;
                        }
                    case WeaponClass.TwoHandedPolearm:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 1000f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.83f) * swingskillModifier);
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.GetWeight(), weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).TotalInertia, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass) * Utilities.thrustSpeedTransfer);
                            thrustSpeedReal = MathF.Ceiling((thrustSpeedReal * 1.05f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 5f) * handlingskillModifier);
                            break;
                        }
                    case WeaponClass.TwoHandedAxe:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 800f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 1000f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 700f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.75f) * swingskillModifier);
                            thrustSpeedReal = MathF.Ceiling((weapon.GetModifiedThrustSpeedForCurrentUsage() * 0.9f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 0.83f) * handlingskillModifier);
                            break;
                        }
                    case WeaponClass.OneHandedSword:
                    case WeaponClass.Dagger:
                    case WeaponClass.TwoHandedSword:
                        {
                            float swingskillModifier = 1f + (effectiveSkillDR / 800f);
                            float thrustskillModifier = 1f + (effectiveSkillDR / 800f);
                            float handlingskillModifier = 1f + (effectiveSkillDR / 800f);

                            swingSpeedReal = MathF.Ceiling((swingSpeed * 0.83f) * swingskillModifier);
                            thrustSpeedReal = MathF.Floor(Utilities.CalculateThrustSpeed(weapon.GetWeight(), weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).TotalInertia, weapon.Item.GetWeaponWithUsageIndex(weaponUsageIndex).CenterOfMass) * Utilities.thrustSpeedTransfer);
                            thrustSpeedReal = MathF.Ceiling((thrustSpeedReal * 1.15f) * thrustskillModifier);
                            handlingReal = MathF.Ceiling((handling * 0.9f) * handlingskillModifier);
                            break;
                        }
                }
            }
        }

        public static float getSwingDamageFactor(WeaponComponentData wcd, ItemModifier itemModifier)
        {
            if (itemModifier == null)
            {
                return wcd.SwingDamageFactor;
            }
            else
            {
                float factorBonus = (itemModifier.ModifyDamage(100) - 100) / 100f;
                return wcd.SwingDamageFactor + factorBonus;
            }
        }

        public static float getThrustDamageFactor(WeaponComponentData wcd, ItemModifier itemModifier)
        {
            if (itemModifier == null)
            {
                return wcd.ThrustDamageFactor;
            }
            else
            {
                float factorBonus = (itemModifier.ModifyDamage(100) - 100) / 100f;
                return wcd.ThrustDamageFactor + factorBonus;
            }
        }

        public static void SimulateThrustLayer(double distance, double usablePower, double maxUsableForce, double mass, out double finalSpeed, out double finalTime)
        {
            double num = 0.0;
            double num2 = 0.01;
            double num3 = 0.0;
            while (num < distance)
            {
                double num4 = usablePower / num2;
                if (num4 > maxUsableForce)
                {
                    num4 = maxUsableForce;
                }
                double num5 = 0.01 * num4 / mass;
                num2 += num5;
                num += num2 * 0.01;
                num3 += 0.01;
            }
            finalSpeed = num2;
            finalTime = num3;
        }

        public static float CalculateThrustSpeed(float _currentWeaponWeight, float inertia, float com)
        {
            float _currentWeaponInertiaAroundGrip = inertia + _currentWeaponWeight * com * com;
            double num = 1.8 + (double)_currentWeaponWeight + (double)_currentWeaponInertiaAroundGrip * 0.2;
            double num2 = 170.0;
            double num3 = 90.0;
            double num4 = 24.0;
            double num5 = 15.0;
            //if (_weaponDescription.WeaponFlags.HasAllFlags(WeaponFlags.MeleeWeapon | WeaponFlags.NotUsableWithOneHand) && !_weaponDescription.WeaponFlags.HasAnyFlag(WeaponFlags.WideGrip))
            //{
            //    num += 0.6;
            //    num5 *= 1.9;
            //    num4 *= 1.1;
            //    num3 *= 1.2;
            //    num2 *= 1.05;
            //}
            //else if (_weaponDescription.WeaponFlags.HasAllFlags(WeaponFlags.MeleeWeapon | WeaponFlags.NotUsableWithOneHand | WeaponFlags.WideGrip))
            //{
            //    num += 0.9;
            //    num5 *= 2.1;
            //    num4 *= 1.2;
            //    num3 *= 1.2;
            //    num2 *= 1.05;
            //}
            SimulateThrustLayer(0.6, 250.0, 48.0, 4.0 + num, out var finalSpeed, out var finalTime);
            SimulateThrustLayer(0.6, num2, num4, 2.0 + num, out var finalSpeed2, out var finalTime2);
            SimulateThrustLayer(0.6, num3, num5, 0.5 + num, out var finalSpeed3, out var finalTime3);
            double num6 = 0.33 * (finalTime + finalTime2 + finalTime3);
            return (float)(3.8500000000000005 / num6);
        }

    }
}