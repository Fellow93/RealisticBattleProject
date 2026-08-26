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
using static TaleWorlds.Core.ArmorComponent;
using static TaleWorlds.Core.ItemObject;

namespace RBMAI
{
    public static partial class Utilities
    {

        public static float swingSpeedTransfer = 4.5454545f;
        public static float thrustSpeedTransfer = 11.7647057f;

        public const float oneHandedPolearmThrustStrength = 2.5f;
        public const float twoHandedPolearmThrustStrength = 5f;

        // Where a formation "is", for AI reasoning.
        //
        // Formation.CachedMedianPosition is the position of ONE soldier -- GetMedianAgent returns whichever unit
        // currently sits closest to the centroid -- so it hops from man to man as the ranks shuffle, and it only
        // refreshes on a ~75-125ms timer, so it steps as well as hops. Comparing distances against it makes
        // near-equidistant choices flip and thresholds flap. SmoothedAverageUnitPosition is the engine's own
        // answer: Formation.Tick lerps it toward the true centroid every tick (dt-correct), so it neither hops
        // nor steps. Native itself treats the average as the formation's centre and keeps the median only as a
        // navmesh carrier (see the Vec3(CachedAveragePosition, CachedMedianPosition.GetNavMeshZ()) idiom
        // throughout FormationQuerySystem).
        //
        // Use these for distance/threshold reasoning. Note the centre is notional: unlike the median it is not
        // guaranteed to be a spot a soldier can stand on, so for a movement DESTINATION use
        // GetFormationCenterWorldPosition, which keeps the median's navmesh face.
        //
        // DELIBERATELY NOT SmoothedAverageUnitPosition, even though that is the smoothest centre and is what the
        // HUD marker uses. Order positions derived from this feed MovementOrder.CreateNewOrderWorldPositionMT,
        // which caches on EXACT Vec2 equality:
        //     if (_getPositionFirstSectionCache.AsVec2 != orderPosition.AsVec2) { ...navmesh queries... }
        // CachedAveragePosition and CachedMedianPosition are cached fields, refreshed only when the formation's
        // ~75-125ms position timer fires, so the same Vec2 recurs for several frames and native takes the cheap
        // cached path. SmoothedAverageUnitPosition is re-lerped EVERY tick and so is never exactly equal twice:
        // it misses that cache every frame for every formation, firing navmesh queries (and possibly the
        // GetAlternatePositionForNavmeshless... search) inside the parallel movement job. That froze the game and
        // crashed it -- a native worker-thread use-after-free -- once another mod (RTSCamera.CommandSystem) was
        // concurrently mutating formation geometry.
        //
        // CachedAveragePosition gives us what we actually wanted -- a centre that does not hop from soldier to
        // soldier like the median -- while keeping native's cache-hit rate exactly as it was.
        public static Vec2 GetFormationCenter(Formation formation)
        {
            if (formation == null)
            {
                return Vec2.Invalid;
            }
            if (formation.CachedAveragePosition.IsValid)
            {
                return formation.CachedAveragePosition;
            }
            return formation.CachedMedianPosition.AsVec2;
        }

        // The formation centre as a WorldPosition, for movement orders. Rides the centre XY on the median
        // agent's navmesh face -- the same trick FormationQuerySystem does at its "formation centre" idiom --
        // so the Z re-resolves off the navmesh when the position is actually consumed.
        //
        // MUST stay free of native calls. This runs on worker threads: MovementOrder.GetPositionAux is invoked
        // from CreateNewOrderWorldPositionMT (the parallel per-formation movement job), as is
        // Formation.GetOrderPositionOfUnit. Copying the median and SetVec2-ing it is pure struct work -- SetVec2
        // on an already-valid position only marks Z invalid -- but anything that forces Z validation
        // (GetNavMesh/GetGroundVec3/GetNavMeshZ) fires a native navmesh query. Off the main thread that is both
        // a hard crash and, run per formation per tick, a stall. Native ships SetVec2MT/ValidateZMT precisely
        // because the plain calls are not concurrency-safe; a navmesh guard here froze and crashed the game.
        //
        // So there is deliberately no off-navmesh guard: callers that need one already apply it themselves on
        // the main thread (see the IsPositionInsideBoundaries / GetNavMesh checks in Behaviours.cs), which is
        // also what unpatched RBM and native both do.
        public static WorldPosition GetFormationCenterWorldPosition(Formation formation)
        {
            WorldPosition median = formation.CachedMedianPosition;
            Vec2 center = GetFormationCenter(formation);
            if (!median.IsValid || !center.IsValid)
            {
                return median;
            }
            median.SetVec2(center);
            return median;
        }

        // Centre-to-centre distance between two formations.
        public static float GetFormationDistance(Formation formation, Formation otherFormation)
        {
            return GetFormationCenter(formation).Distance(GetFormationCenter(otherFormation));
        }

        // Distance from a point to a formation's centre.
        public static float GetDistanceToFormation(Vec2 position, Formation formation)
        {
            return position.Distance(GetFormationCenter(formation));
        }

        // A "banner" can reach the game as either WeaponClass.Banner (vanilla / decorative banners) or as a regular
        // melee weapon class such as TwoHandedPolearm (Raise Your Banner's "spear banner" variants). Checking
        // WeaponClass alone misses the polearm variants, so every banner item is also matched by item_usage="banner".
        public static bool IsBannerWeapon(MissionWeapon weapon)
        {
            if (weapon.IsEmpty || weapon.CurrentUsageItem == null)
            {
                return false;
            }
            if (weapon.CurrentUsageItem.WeaponClass == WeaponClass.Banner)
            {
                return true;
            }
            string itemUsage = weapon.CurrentUsageItem.ItemUsage;
            if (itemUsage != null && itemUsage.Contains("banner"))
            {
                return true;
            }
            // Bulletproof fallback: every Raise Your Banner item id begins with "RYB_" (StringId == the XML item id),
            // including the TwoHandedPolearm "spear banner" variants that share no banner WeaponClass.
            ItemObject item = weapon.Item;
            return item != null && item.StringId != null && item.StringId.StartsWith("RYB_");
        }

        public static bool IsBannerBearer(Agent agent)
        {
            if (agent == null)
            {
                return false;
            }
            return IsBannerWeapon(agent.WieldedOffhandWeapon) || IsBannerWeapon(agent.WieldedWeapon);
        }

        public static float FormationRatioWieldingShockWeapons(Formation formation)
        {
            float result = 0f;
            float countOfAgents = 0f;
            float countOfAgentsWieldingShockWeapons = 0f;
            formation.ApplyActionOnEachUnit(delegate (Agent agent)
            {
                if (agent.IsActive())
                {
                    countOfAgents++;
                    if (!agent.WieldedWeapon.IsEmpty && agent.WieldedWeapon.CurrentUsageItem != null)
                    {
                        if (agent.WieldedWeapon.CurrentUsageItem.WeaponClass == WeaponClass.TwoHandedAxe ||
                        agent.WieldedWeapon.CurrentUsageItem.WeaponClass == WeaponClass.TwoHandedMace ||
                        agent.WieldedWeapon.CurrentUsageItem.WeaponClass == WeaponClass.TwoHandedSword)
                        {
                            countOfAgentsWieldingShockWeapons++;
                        }
                        else if (agent.WieldedWeapon.CurrentUsageItem.WeaponClass == WeaponClass.TwoHandedPolearm && agent.WieldedWeapon.CurrentUsageItem.SwingSpeed != 0)
                        {
                            countOfAgentsWieldingShockWeapons++;
                        }
                    }
                }
            });
            result = countOfAgentsWieldingShockWeapons / countOfAgents;
            return result;
        }

        public static float FormationRatioShieldWallEligible(Formation formation)
        {
            float result = 0f;
            float countOfAgents = 0f;
            float countOfAgentsWieldingLargeShield = 0f;
            formation.ApplyActionOnEachUnit(delegate (Agent agent)
            {
                if (agent.IsActive())
                {
                    countOfAgents++;
                    if (!agent.WieldedOffhandWeapon.IsEmpty && agent.WieldedWeapon.CurrentUsageItem != null && agent.WieldedOffhandWeapon.CurrentUsageItem.WeaponClass == WeaponClass.LargeShield)
                    {
                        int ammoAmount = 0;
                        for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
                        {
                            if (agent.Equipment != null && !agent.Equipment[equipmentIndex].IsEmpty && !agent.Equipment[equipmentIndex].IsShield())
                            {
                                ammoAmount += agent.Equipment[equipmentIndex].Amount;
                            }
                        }
                        if (ammoAmount <= 1)
                        {
                            countOfAgentsWieldingLargeShield++;
                        }
                    }
                }
            });
            result = countOfAgentsWieldingLargeShield / countOfAgents;
            return result;
        }

        public static void DecideArrangementOrderForFormation(Formation formation)
        {
            bool isShock = FormationRatioWieldingShockWeapons(formation) > 0.5f;
            bool isShieldWallEligible = FormationRatioShieldWallEligible(formation) > 0.7f;
            if (isShieldWallEligible)
            {
                formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderShieldWall);
                return;
            }
            if (isShock)
            {
                formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLoose);
                return;
            }
            formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderLine);
            return;
        }

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
                        float distance = GetFormationDistance(mainInfantry, enemyForamtion) + mainInfantry.Depth / 2f + enemyForamtion.Depth / 2f;
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
                    if (agent.LastRangedHitTime > 0f && currentTime > agent.LastRangedHitTime && (currentTime - agent.LastRangedHitTime) < (lastAttackTimeTreshold + (20f * ratioOfCrossbowmen)))
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
                    //if (currentTime - agent.LastRangedHitTime < 6f)
                    //{
                    //    countOfSkirmishers++;
                    //}
                    bool isActiveSkrimisher = false;
                    float countedUnits = 0f;
                    //float currentTime = Mission.Current.CurrentTime;
                    float currentTime = MBCommon.GetTotalMissionTime();
                    if (agent.LastRangedHitTime > 0f && currentTime - agent.LastRangedHitTime < 6f && currentTime > agent.LastRangedHitTime && ratio <= desiredRatio && ((float)countedUnits / (float)formation.CountOfUnits) <= desiredRatio)
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
                    float lastMeleeAttackTime = agent.LastMeleeHitTime;
                    float lastMeleeHitTime = agent.LastRecievedMeleeHitTime;
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
                        float newDist = GetDistanceToFormation(position.AsVec2, enemyFormation);
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
                        float newDist = GetDistanceToFormation(position.AsVec2, enemyFormation);
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
                            float newDist = GetDistanceToFormation(position.AsVec2, significantFormation);
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
                            float newDist = GetDistanceToFormation(position.AsVec2, significantFormation);
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
                        float newDist = GetFormationDistance(formation, enemyFormation);
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
                            float distance = GetFormationDistance(formation, significantFormation);
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
                            float newDist = GetFormationDistance(formation, enemyFormation);
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
                            float newDist = GetFormationDistance(formation, enemyFormation);
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
                                    float newDist = GetFormationDistance(formation, allyFormation);
                                    if (newDist < dist)
                                    {
                                        significantAlly = allyFormation;
                                        dist = newDist;
                                    }
                                }
                                if (formation != null && includeRanged && allyFormation.CountOfUnits > 0 && allyFormation.QuerySystem.IsRangedFormation)
                                {
                                    float newDist = GetFormationDistance(formation, allyFormation);
                                    if (newDist < dist)
                                    {
                                        significantAlly = allyFormation;
                                        dist = newDist;
                                    }
                                }
                                if (formation != null && includeCavalry && allyFormation.CountOfUnits > 0 && allyFormation.QuerySystem.IsCavalryFormation && !allyFormation.QuerySystem.IsRangedCavalryFormation)
                                {
                                    float newDist = GetFormationDistance(formation, allyFormation);
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

        public static float GetPowerOfAgentsSum(IEnumerable<Agent> agents)
        {
            float result = 0f;
            foreach (Agent agent in agents)
            {
                result += MBMath.ClampInt((int)Math.Floor(agent.CharacterPowerCached * 65), 75, 200);
            }
            return result;
        }

        private static float GetPowerOriginal(int tier, bool isHero = false, bool isMounted = false)
        {
            return (float)((2 + tier) * (8 + tier)) * 0.02f * (isHero ? 1.5f : (isMounted ? 1.2f : 1f));
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
    }
}
