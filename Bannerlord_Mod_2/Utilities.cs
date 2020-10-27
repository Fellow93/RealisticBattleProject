using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ItemObject;

namespace RealisticBattle
{
    public static class Utilities
    {
        public static int calculateMissileSpeed(float ammoWeight, MissionWeapon rangedWeapon, int drawWeight)
        {
            int calculatedMissileSpeed = 10;
            if (rangedWeapon.CurrentUsageItem.ItemUsage.Equals("bow"))
            {
                float drawlength = (28 * 0.0254f);
                double potentialEnergy = 0.5f * (drawWeight * 4.448f) * drawlength;
                calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt(((potentialEnergy * 2f) / ammoWeight) * 0.91f * ((ammoWeight * 3f) + 0.432f)));
            }
            else if (rangedWeapon.CurrentUsageItem.ItemUsage.Equals("long_bow"))
            {
                float drawlength = (30 * 0.0254f);
                double potentialEnergy = 0.5f * (drawWeight * 4.448f) * drawlength;
                calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt(((potentialEnergy * 2f) / ammoWeight) * 0.89f * ((ammoWeight * 3.3f) + 0.33f) * (1f + (0.416f - (0.0026 * drawWeight)))));
            }
            else if (rangedWeapon.CurrentUsageItem.ItemUsage.Equals("crossbow") || rangedWeapon.CurrentUsageItem.ItemUsage.Equals("crossbow_fast"))
            {
                float drawlength = (4.5f * 0.0254f);
                double potentialEnergy = 0.5f * (drawWeight * 4.448f) * drawlength;
                calculatedMissileSpeed = (int)Math.Floor(Math.Sqrt(((potentialEnergy * 2f) / ammoWeight) * 0.45f));
            }
            return calculatedMissileSpeed;
        }

        public static int calculateThrowableSpeed(float ammoWeight)
        {
            int calculatedThrowingSpeed = (int)Math.Ceiling(Math.Sqrt(160f * 2f / ammoWeight));
            //calculatedThrowingSpeed += 7;
            return calculatedThrowingSpeed;
        }

        public static void assignThrowableMissileSpeed(MissionWeapon throwable, int index, int correctiveMissileSpeed)
        {
            float ammoWeight = throwable.GetWeight() / throwable.Amount;
            int calculatedThrowingSpeed = Utilities.calculateThrowableSpeed(ammoWeight);
            PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
            property.DeclaringType.GetProperty("MissileSpeed");
            throwable.CurrentUsageIndex = index;
            calculatedThrowingSpeed += correctiveMissileSpeed;
            property.SetValue(throwable.CurrentUsageItem, calculatedThrowingSpeed, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
            throwable.CurrentUsageIndex = 0;
        }

        public static void assignStoneMissileSpeed(MissionWeapon throwable, int index)
        {
            PropertyInfo property = typeof(WeaponComponentData).GetProperty("MissileSpeed");
            property.DeclaringType.GetProperty("MissileSpeed");
            throwable.CurrentUsageIndex = index;
            property.SetValue(throwable.CurrentUsageItem, 25, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
            throwable.CurrentUsageIndex = 0;
        }

        public static bool HasBattleBeenJoined(Formation mainInfantry, bool hasBattleBeenJoined, float battleJoinRange)
        {
            if (mainInfantry != null)
            {
                if (FormationFightingInMelee(mainInfantry))
                {
                    return true;
                }
                else
                {
                    //FormationQuerySystem cslef = mainInfantry.QuerySystem.ClosestSignificantlyLargeEnemyFormation;
                    Formation enemyForamtion = Utilities.FindSignificantEnemy(mainInfantry, true, true, false, false, false);
                    if(enemyForamtion != null)
                    {
                        float distanceSpeedValue = mainInfantry.QuerySystem.MedianPosition.AsVec2.Distance(enemyForamtion.QuerySystem.MedianPosition.AsVec2) / enemyForamtion.QuerySystem.MovementSpeedMaximum;
                        if (distanceSpeedValue <= 5f)
                        {
                            mainInfantry.FiringOrder = FiringOrder.FiringOrderHoldYourFire;
                        }
                        return (distanceSpeedValue <= (battleJoinRange + (hasBattleBeenJoined ? 10f : 0f)));
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
                PropertyInfo property = typeof(Formation).GetProperty("arrangement", BindingFlags.NonPublic | BindingFlags.Instance);
                if (property != null)
                {
                    property.DeclaringType.GetProperty("arrangement");
                    IFormationArrangement arrangement = (IFormationArrangement)property.GetValue(formation);

                    FieldInfo field = typeof(LineFormation).GetField("_allUnits", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        field.DeclaringType.GetField("_allUnits");
                        List<IFormationUnit> agents = (List<IFormationUnit>)field.GetValue(arrangement);

                        foreach (Agent agent in agents.ToList())
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
                        }

                        float mountedSkirmishersRatio = (float)mountedSkirmishersCount / (float)formation.CountOfUnits;
                        if (mountedSkirmishersRatio > 0.6f)
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
            else
            {
                return false;
            }
        }

        public static Agent NearestAgentFromFormation(Vec2 unitPosition, Formation targetFormation)
        {
            Agent targetAgent = null;
            float distance = 10000f;
            PropertyInfo property = typeof(Formation).GetProperty("arrangement", BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null)
            {
                property.DeclaringType.GetProperty("arrangement");
                IFormationArrangement arrangement = (IFormationArrangement)property.GetValue(targetFormation);

                FieldInfo field = typeof(LineFormation).GetField("_allUnits", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    field.DeclaringType.GetField("_allUnits");
                    List<IFormationUnit> agents = (List<IFormationUnit>)field.GetValue(arrangement);

                    foreach (Agent agent in agents.ToList())
                    {
                        float newDist = unitPosition.Distance(agent.GetWorldPosition().AsVec2);
                        if (newDist < distance)
                        {
                            targetAgent = agent;
                            distance = newDist;
                        }
                    }
                }
            }
            return targetAgent;
        }
        public static bool FormationFightingInMelee(Formation formation)
        {
            bool fightingInMelee = false;
            PropertyInfo property = typeof(Formation).GetProperty("arrangement", BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null)
            {
                property.DeclaringType.GetProperty("arrangement");
                IFormationArrangement arrangement = (IFormationArrangement)property.GetValue(formation);

                FieldInfo field = typeof(LineFormation).GetField("_allUnits", BindingFlags.NonPublic | BindingFlags.Instance);
                float currentTime = MBCommon.TimeType.Mission.GetTime();
                float countOfUnits = 0;
                float countOfUnitsFightingInMelee = 0;
                if (field != null)
                {
                    field.DeclaringType.GetField("_allUnits");
                    List<IFormationUnit> agents = (List<IFormationUnit>)field.GetValue(arrangement);

                    foreach (Agent agent in agents.ToList())
                    {
                        countOfUnits++;
                        float lastMeleeAttackTime = agent.LastMeleeAttackTime;
                        float lastMeleeHitTime = agent.LastMeleeHitTime;
                        if ((currentTime - lastMeleeAttackTime < 4f) || (currentTime - lastMeleeHitTime < 4f))
                        {
                            countOfUnitsFightingInMelee++;
                        }
                    }
                    if (countOfUnitsFightingInMelee / countOfUnits > 0.5f)
                    {
                        fightingInMelee = true;
                    }
                }
            }
            return fightingInMelee;
        }

        public static Formation FindSignificantEnemy(Formation formation, bool includeInfantry, bool includeRanged, bool includeCavalry, bool includeMountedSkirmishers, bool includeHorseArchers)
        {
            Formation significantEnemy = null;
            float dist = 10000f;

            foreach (Team team in Mission.Current.Teams.ToList())
            {
                if (team.IsEnemyOf(formation.Team))
                {
                    foreach (Formation enemyFormation in team.Formations.ToList())
                    {
                        if (includeInfantry && enemyFormation.QuerySystem.IsInfantryFormation)
                        {
                            float newDist = formation.QuerySystem.MedianPosition.AsVec2.Distance(enemyFormation.QuerySystem.MedianPosition.AsVec2);
                            if (newDist < dist)
                            {
                                significantEnemy = enemyFormation;
                                dist = newDist;
                            }
                        }
                        if (includeRanged && enemyFormation.QuerySystem.IsRangedFormation)
                        {
                            float newDist = formation.QuerySystem.MedianPosition.AsVec2.Distance(enemyFormation.QuerySystem.MedianPosition.AsVec2);
                            if (newDist < dist)
                            {
                                significantEnemy = enemyFormation;
                                dist = newDist;
                            }
                        }
                        if (includeCavalry && enemyFormation.QuerySystem.IsCavalryFormation && !CheckIfMountedSkirmishFormation(enemyFormation))
                        {
                            float newDist = formation.QuerySystem.MedianPosition.AsVec2.Distance(enemyFormation.QuerySystem.MedianPosition.AsVec2);
                            if (newDist < dist)
                            {
                                significantEnemy = enemyFormation;
                                dist = newDist;
                            }
                        }
                        if (includeMountedSkirmishers && CheckIfMountedSkirmishFormation(enemyFormation))
                        {
                            float newDist = formation.QuerySystem.MedianPosition.AsVec2.Distance(enemyFormation.QuerySystem.MedianPosition.AsVec2);
                            if (newDist < dist)
                            {
                                significantEnemy = enemyFormation;
                                dist = newDist;
                            }
                        }
                        if (includeHorseArchers && enemyFormation.QuerySystem.IsRangedCavalryFormation)
                        {
                            float newDist = formation.QuerySystem.MedianPosition.AsVec2.Distance(enemyFormation.QuerySystem.MedianPosition.AsVec2);
                            if (newDist < dist)
                            {
                                significantEnemy = enemyFormation;
                                dist = newDist;
                            }
                        }
                    }
                }
            }
            return significantEnemy;
        }
    }
}

