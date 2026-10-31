using RBMConfig;
using System;
using System.Collections.Concurrent;
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
        // Where a formation "is", for AI reasoning: CachedAveragePosition (not the median, which is ONE soldier and hops as ranks shuffle).
        // The centre is notional (may not be standable); for a movement DESTINATION use GetFormationCenterWorldPosition.
        // DELIBERATELY NOT SmoothedAverageUnitPosition: order positions feed MovementOrder.CreateNewOrderWorldPositionMT, which caches on
        // EXACT Vec2 equality; that field is re-lerped every tick, so it would fire navmesh queries per formation inside the parallel
        // movement job (froze/crashed the game: native worker use-after-free with another mod mutating formation geometry).
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

        // The formation centre as a WorldPosition for movement orders: centre XY on the median agent's navmesh face (Z re-resolves on use).
        // MUST stay free of native calls: runs on worker threads (CreateNewOrderWorldPositionMT, GetOrderPositionOfUnit). SetVec2 on a valid
        // position only marks Z invalid, but GetNavMesh/GetGroundVec3/GetNavMeshZ force a native navmesh query (crash/stall off the main thread).
        // So deliberately no off-navmesh guard: callers apply it on the main thread.
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

        public static float GetFormationDistance(Formation formation, Formation otherFormation)
        {
            return GetFormationCenter(formation).Distance(GetFormationCenter(otherFormation));
        }

        public static float GetDistanceToFormation(Vec2 position, Formation formation)
        {
            return position.Distance(GetFormationCenter(formation));
        }

        // A banner is either WeaponClass.Banner or a melee class (Raise Your Banner "spear banner" variants), so also match item_usage="banner".
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
            // Fallback: every Raise Your Banner item id begins with "RYB_" (incl. spear banner variants with no banner WeaponClass).
            ItemObject item = weapon.Item;
            return item != null && item.StringId != null && item.StringId.StartsWith("RYB_", StringComparison.Ordinal);
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
                    // Judged by the weapon in hand, not the kit: a large-shield man counts unless holding a throwing weapon now.
                    if (!agent.WieldedOffhandWeapon.IsEmpty && agent.WieldedWeapon.CurrentUsageItem != null && agent.WieldedOffhandWeapon.CurrentUsageItem.WeaponClass == WeaponClass.LargeShield
                        && !agent.WieldedWeapon.IsAnyConsumable())
                    {
                        countOfAgentsWieldingLargeShield++;
                    }
                }
            });
            result = countOfAgentsWieldingLargeShield / countOfAgents;
            return result;
        }

        /// <summary>
        /// User rule: RBM never puts a RANGED formation in a Square, Circle or ShieldWall. True for a ranged / horse-archer formation or
        /// one at least half missile men. Only ChooseRangedArrangement may give one a (loose) Circle; the game's own behaviors are not filtered.
        /// Query-system reads only (AI-thread safe).
        /// </summary>
        public static bool IsRangedForArrangement(Formation formation)
        {
            if (formation == null || formation.CountOfUnits <= 0)
            {
                return false;
            }
            FormationQuerySystem q = formation.QuerySystem;
            return q.IsRangedFormation || q.IsRangedCavalryFormation || q.RangedUnitRatio + q.RangedCavalryUnitRatio >= 0.5f;
        }

        public static void DecideArrangementOrderForFormation(Formation formation)
        {
            // Hysteresis: the shield wall is entered above the "Shield wall share" setting and held while above it minus 0.1
            // (every flip re-shapes the line). Never for a ranged formation (IsRangedForArrangement).
            ArrangementOrder.ArrangementOrderEnum current = formation.ArrangementOrder.OrderEnum;
            bool isShock = FormationRatioWieldingShockWeapons(formation) > (current == ArrangementOrder.ArrangementOrderEnum.Loose ? 0.4f : 0.5f);
            float shieldWallShare = RBMConfig.RBMConfig.shieldWallShare;
            bool isShieldWallEligible = !IsRangedForArrangement(formation)
                && FormationRatioShieldWallEligible(formation) > (current == ArrangementOrder.ArrangementOrderEnum.ShieldWall ? shieldWallShare - 0.1f : shieldWallShare);
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

        private static readonly PropertyInfo _lineFileCount =
            typeof(LineFormation).GetProperty("FileCount", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        // Bounds-checked slot lookup (native indexes _units2D unchecked; a stale file/rank index throws IndexOutOfRange). Main thread only.
        public static bool TryGetUnitSlot(Formation formation, Agent agent, out WorldPosition slot)
        {
            slot = WorldPosition.Invalid;
            if (formation == null || agent == null || !agent.IsActive() || agent.Formation != formation)
            {
                return false;
            }
            IFormationUnit unit = agent;
            int file = unit.FormationFileIndex;
            int rank = unit.FormationRankIndex;
            if (file < 0 || rank < 0)
            {
                return false;
            }
            LineFormation line = formation.Arrangement as LineFormation;
            if (line != null)
            {
                if (rank >= line.RankCount)
                {
                    return false;
                }
                if (_lineFileCount != null && file >= (int)_lineFileCount.GetValue(line, null))
                {
                    return false;
                }
            }
            slot = formation.GetOrderPositionOfUnit(agent);
            return slot.IsValid;
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
                        float edgeGap = FormationGeometry.Gap(mainInfantry, FootprintMode.ScatterAware, enemyForamtion, FootprintMode.ScatterAware);
                        return (edgeGap <= (battleJoinRange + (hasBattleBeenJoined ? 10f : 0f)));
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// HasBattleBeenJoined for a tactic whose main infantry may be missing: while the team has a FormationCohesion.TeamMain, the old test. Otherwise (no
        /// melee-infantry main; a token infantry formation the native tactic still passes as its main included) measured from the team's position anchor
        /// (ArmyComposition): joined when the enemy is only cavalry, the anchor is in melee, an archer anchor is dry (ArmyComposition.IsDry), or a significant
        /// enemy of any class is within range (+10 m once joined). No anchor (all mounted / token foot): joined, as before. An ArmyScale tiny side keeps the old test.
        /// </summary>
        public static bool HasBattleBeenJoined(Team team, Formation mainInfantry, bool hasBattleBeenJoined, float battleJoinRange = 75f)
        {
            if (!UsesAnchor(team))
            {
                return HasBattleBeenJoined(mainInfantry, hasBattleBeenJoined, battleJoinRange);
            }
            Formation anchor = ArmyComposition.PositionAnchor(team);
            if (anchor == null || anchor.CountOfUnits <= 0 || CheckIfOnlyCavRemaining(anchor))
            {
                return true;
            }
            // the anchor is never mounted, so a dry anchor is a dry archer anchor
            if (FormationFightingInMelee(anchor, ArmyComposition.InMelee) || ArmyComposition.IsDry(anchor))
            {
                return true;
            }
            float gap = NearestSignificantEnemyGap(anchor);
            return gap <= battleJoinRange + (hasBattleBeenJoined ? 10f : 0f);
        }

        /// <summary>The anchor path for a team with no melee-infantry main (FormationCohesion.TeamMain), whatever the tactic passes (native tactics pass a token
        /// infantry group as their main); a TeamMain (e.g. player-led, not the tactic's) keeps the old test, and so does an ArmyScale tiny side.</summary>
        private static bool UsesAnchor(Team team)
        {
            if (team == null || FormationCohesion.TeamMain(team) != null)
            {
                return false;
            }
            ArmyScale.SideScale scale = ArmyScale.For(team.Side);
            return !(scale != null && scale.ReactionsOff);
        }

        /// <summary>AI thread: edge gap (ScatterAware) from the formation to the nearest enemy formation significant to it; MaxValue without one.</summary>
        internal static float NearestSignificantEnemyGap(Formation formation)
        {
            float best = float.MaxValue;
            Mission mission = Mission.Current;
            if (formation?.Team == null || mission == null)
            {
                return best;
            }
            Mission.TeamCollection teams = mission.Teams;
            for (int t = 0; t < teams.Count; t++)
            {
                Team team = teams[t];
                if (team == null || !team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                MBList<Formation> formations = team.FormationsIncludingEmpty;
                for (int i = 0; i < formations.Count; i++)
                {
                    Formation e = formations[i];
                    if (e == null || e.CountOfUnits <= 0 || !ThreatSignificance.IsSignificant(formation, e))
                    {
                        continue;
                    }
                    float gap = FormationGeometry.Gap(formation, FootprintMode.ScatterAware, e, FootprintMode.ScatterAware);
                    if (gap < best)
                    {
                        best = gap;
                    }
                }
            }
            return best;
        }

        private sealed class AttackJoinLatch
        {
            public bool Joined;
            public float JoinedAt = float.NaN;
            public float QuietSince = float.NaN;
        }

        private static readonly ConcurrentDictionary<Team, AttackJoinLatch> _attackJoinLatches = new ConcurrentDictionary<Team, AttackJoinLatch>();

        private const float AttackJoinHoldSeconds = 20f;

        // Unjoin only after the edge gap to the significant enemy stayed above this (m), with under AttackUnjoinMelee in melee, for AttackUnjoinSeconds.
        private const float AttackUnjoinGap = 120f;

        private const float AttackUnjoinMelee = 0.1f;

        private const float AttackUnjoinSeconds = 10f;

        /// <summary>The main infantry is fighting in melee, or a significant enemy formation exists for it to join. Without one, the same for the position anchor (any enemy class).</summary>
        private static bool HasEnemyToJoin(Formation mainInfantry, Team team)
        {
            if (UsesAnchor(team))
            {
                Formation anchor = ArmyComposition.PositionAnchor(team);
                return anchor != null && anchor.CountOfUnits > 0
                    && (FormationFightingInMelee(anchor, ArmyComposition.InMelee) || NearestSignificantEnemyGap(anchor) < float.MaxValue);
            }
            if (mainInfantry == null || mainInfantry.CountOfUnits <= 0)
            {
                return false;
            }
            return FormationFightingInMelee(mainInfantry, 0.35f)
                || FindSignificantEnemy(mainInfantry, true, true, false, false, false, true) != null;
        }

        internal static void ClearAttackJoinLatches()
        {
            _attackJoinLatches.Clear();
        }

        /// <summary>
        /// HasBattleBeenJoined latched per team for the attack tactics (the plain test's +10 m hysteresis flipped the army on kiting skirmishers).
        /// Stays joined for AttackJoinHoldSeconds, then until the edge gap exceeds AttackUnjoinGap with under 10 % in melee for AttackUnjoinSeconds.
        /// Team tick thread; one latch per team, written only by that team's tactic.
        /// </summary>
        public static bool HasAttackBeenJoined(Formation mainInfantry, Team team, bool hasBattleBeenJoined, float battleJoinRange = 75f)
        {
            Mission mission = Mission.Current;
            if (team == null || mission == null)
            {
                return HasBattleBeenJoined(mainInfantry, hasBattleBeenJoined, battleJoinRange);
            }
            AttackJoinLatch latch = _attackJoinLatches.GetOrAdd(team, t => new AttackJoinLatch());
            float now = mission.CurrentTime;
            if (HasBattleBeenJoined(team, mainInfantry, hasBattleBeenJoined || latch.Joined, battleJoinRange))
            {
                // HasBattleBeenJoined is also true with no enemy formation (team's first decision, before spawn): only a join against a real enemy latches.
                if (!latch.Joined && !HasEnemyToJoin(mainInfantry, team))
                {
                    return true;
                }
                if (!latch.Joined)
                {
                    latch.Joined = true;
                    latch.JoinedAt = now;
                }
                latch.QuietSince = float.NaN;
                return true;
            }
            if (!latch.Joined)
            {
                return false;
            }
            if (now - latch.JoinedAt < AttackJoinHoldSeconds)
            {
                return true;
            }
            bool quiet = true;
            Formation anchor = UsesAnchor(team) ?ArmyComposition.PositionAnchor(team) : null;
            if (anchor != null && anchor.CountOfUnits > 0)
            {
                quiet = !FormationFightingInMelee(anchor, AttackUnjoinMelee) && NearestSignificantEnemyGap(anchor) > AttackUnjoinGap;
            }
            else if (mainInfantry != null && mainInfantry.CountOfUnits > 0)
            {
                if (FormationFightingInMelee(mainInfantry, AttackUnjoinMelee))
                {
                    quiet = false;
                }
                else
                {
                    Formation enemy = FindSignificantEnemy(mainInfantry, true, true, false, false, false, true);
                    if (enemy != null)
                    {
                        quiet = FormationGeometry.Gap(mainInfantry, FootprintMode.ScatterAware, enemy, FootprintMode.ScatterAware) > AttackUnjoinGap;
                    }
                }
            }
            if (!quiet)
            {
                latch.QuietSince = float.NaN;
                return true;
            }
            if (float.IsNaN(latch.QuietSince))
            {
                latch.QuietSince = now;
            }
            if (now - latch.QuietSince < AttackUnjoinSeconds)
            {
                return true;
            }
            latch.Joined = false;
            latch.JoinedAt = float.NaN;
            latch.QuietSince = float.NaN;
            return false;
        }

        // Advancing lines must be wider than deep: files must exceed ranks by at least 2.
        // Engine formula (LineFormation.GetFileCountFromWidth): files f = floor((width - UnitDiameter) / (Interval + UnitDiameter)) + 1;
        // inverse: width = (f - 1) * (Interval + UnitDiameter) + UnitDiameter; ranks r = ceil(n / f), n = CountOfUnitsWithoutDetachedOnes.
        // Main-thread behavior/tactic ticks only, never the movement worker job (QuerySystem-backed properties).
        public static float EnforceMinFileWidth(Formation formation, float desiredWidth)
        {
            if (formation == null || desiredWidth <= 0f)
            {
                return desiredWidth;
            }

            Mission mission = Mission.Current;
            if (mission == null || mission.IsSiegeBattle || mission.IsSallyOutBattle)
            {
                return desiredWidth;
            }

            // Cavalry and horse archers keep whatever frontage their behavior asked for.
            if (formation.QuerySystem.IsCavalryFormation || formation.QuerySystem.IsRangedCavalryFormation)
            {
                return desiredWidth;
            }

            // Line-type arrangements only -- circle/square/skein/scatter/column derive their shape differently.
            ArrangementOrder.ArrangementOrderEnum arrangement = formation.ArrangementOrder.OrderEnum;
            if (arrangement != ArrangementOrder.ArrangementOrderEnum.Line
                && arrangement != ArrangementOrder.ArrangementOrderEnum.ShieldWall
                && arrangement != ArrangementOrder.ArrangementOrderEnum.Loose)
            {
                return desiredWidth;
            }

            int n = formation.CountOfUnitsWithoutDetachedOnes;
            if (n <= 2)
            {
                return desiredWidth;
            }

            float spacing = formation.Interval + formation.UnitDiameter;
            if (spacing <= 0.01f)
            {
                return desiredWidth;
            }

            int files = MathF.Max(1, (int)((desiredWidth - formation.UnitDiameter) / spacing) + 1);
            int ranks = MathF.Ceiling(n / (float)files);
            if (files >= ranks + 2)
            {
                return desiredWidth;
            }

            // Smallest file count with f >= ceil(n / f) + 2.
            int fixedFiles = files;
            while (fixedFiles < n && fixedFiles < MathF.Ceiling(n / (float)fixedFiles) + 2)
            {
                fixedFiles++;
            }

            float fixedWidth = (fixedFiles - 1) * spacing + formation.UnitDiameter;
            fixedWidth = MathF.Max(desiredWidth, fixedWidth);

            float maximumWidth = formation.MaximumWidth;
            if (maximumWidth > 0f && fixedWidth > maximumWidth)
            {
                fixedWidth = MathF.Max(desiredWidth, maximumWidth);
            }
            return fixedWidth;
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
                    float currentTime = MBCommon.GetTotalMissionTime();
                    if (agent.LastRangedHitTime > 0f && currentTime > agent.LastRangedHitTime && (currentTime - agent.LastRangedHitTime) < (lastAttackTimeTreshold + (20f * ratioOfCrossbowmen)))
                    {
                        countOfShooting++;
                    }
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
                    bool isActiveSkrimisher = false;
                    float countedUnits = 0f;
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

        // One FormationFightingInMelee answer per formation and ratio per mission-time stamp, reused only at the exact same time and
        // unit count. Immutable entries replaced whole (AI and main thread). Cleared in MissionStartReset.
        internal sealed class MeleeTestEntry
        {
            public readonly float Time;
            public readonly int Count;
            public readonly bool Result;

            public MeleeTestEntry(float time, int count, bool result)
            {
                Time = time;
                Count = count;
                Result = result;
            }
        }

        internal static readonly ConcurrentDictionary<(Formation, float), MeleeTestEntry> fightingInMeleeCache = new ConcurrentDictionary<(Formation, float), MeleeTestEntry>();

        public static bool FormationFightingInMelee(Formation formation, float desiredRatio)
        {
            float currentTime = MBCommon.GetTotalMissionTime();
            int count = formation.CountOfUnits;
            if (fightingInMeleeCache.TryGetValue((formation, desiredRatio), out MeleeTestEntry cached) && cached.Time == currentTime && cached.Count == count)
            {
                return cached.Result;
            }
            bool result = FormationFightingInMeleeWalk(formation, desiredRatio, currentTime);
            fightingInMeleeCache[(formation, desiredRatio)] = new MeleeTestEntry(currentTime, count, result);
            return result;
        }

        private static bool FormationFightingInMeleeWalk(Formation formation, float desiredRatio, float currentTime)
        {
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
            Mission.TeamCollection teams = Mission.Current.Teams;
            for (int t = 0; t < teams.Count; t++)
            {
                Team team = teams[t];
                if (team.IsEnemyOf(formation.Team))
                {
                    if (CountFormationsWithUnits(team, out Formation only) == 1)
                    {
                        formations.Add(only);
                        return formations;
                    }
                    MBList<Formation> teamFormations = team.FormationsIncludingEmpty;
                    for (int i = 0; i < teamFormations.Count; i++)
                    {
                        Formation enemyFormation = teamFormations[i];
                        if (enemyFormation.CountOfUnits <= 0)
                        {
                            continue;
                        }
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

        // Count of the team's formations with men and the first of them (null with none). Plain loop; the list is filled once at team
        // creation, so index reads are safe off the main thread.
        private static int CountFormationsWithUnits(Team team, out Formation first)
        {
            first = null;
            int count = 0;
            MBList<Formation> teamFormations = team.FormationsIncludingEmpty;
            for (int i = 0; i < teamFormations.Count; i++)
            {
                Formation f = teamFormations[i];
                if (f.CountOfUnits > 0)
                {
                    if (count == 0)
                    {
                        first = f;
                    }
                    count++;
                }
            }
            return count;
        }

        // Appends the team's formations with men to `into`, in FormationsIncludingEmpty order.
        private static void AddFormationsWithUnits(Team team, List<Formation> into)
        {
            MBList<Formation> teamFormations = team.FormationsIncludingEmpty;
            for (int i = 0; i < teamFormations.Count; i++)
            {
                Formation f = teamFormations[i];
                if (f.CountOfUnits > 0)
                {
                    into.Add(f);
                }
            }
        }

        // GetCorrectTarget asks per charging agent from the parallel movement job: built once per formation per frame. Entries are
        // replaced whole, never mutated (a racing double build is harmless). Cleared in MissionStartReset.
        internal sealed class SignificantFormationsEntry
        {
            public readonly float Time;
            public readonly List<Formation> Formations;

            public SignificantFormationsEntry(float time, List<Formation> formations)
            {
                Time = time;
                Formations = formations;
            }
        }

        internal static readonly ConcurrentDictionary<Formation, SignificantFormationsEntry> significantFormationsCache = new ConcurrentDictionary<Formation, SignificantFormationsEntry>();

        // FindSignificantFormations(formation) (no cavalry), cached per frame. The list is shared: read only.
        public static List<Formation> FindSignificantFormationsCached(Formation formation)
        {
            Mission mission = Mission.Current;
            if (mission == null || formation == null)
            {
                return FindSignificantFormations(formation);
            }
            float now = mission.CurrentTime;
            if (significantFormationsCache.TryGetValue(formation, out SignificantFormationsEntry entry) && entry.Time == now)
            {
                return entry.Formations;
            }
            List<Formation> formations = FindSignificantFormations(formation);
            significantFormationsCache[formation] = new SignificantFormationsEntry(now, formations);
            return formations;
        }

        public static List<Formation> FindSignificantArcherFormations(Formation formation)
        {
            List<Formation> formations = new List<Formation>();
            if (formation != null)
            {
                Mission.TeamCollection teams = Mission.Current.Teams;
                for (int t = 0; t < teams.Count; t++)
                {
                    Team team = teams[t];
                    if (team.IsEnemyOf(formation.Team))
                    {
                        if (CountFormationsWithUnits(team, out Formation only) == 1)
                        {
                            formations.Add(only);
                            return formations;
                        }
                        MBList<Formation> teamFormations = team.FormationsIncludingEmpty;
                        for (int i = 0; i < teamFormations.Count; i++)
                        {
                            Formation enemyFormation = teamFormations[i];
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
                Mission.TeamCollection teams = Mission.Current.Teams;
                for (int t = 0; t < teams.Count; t++)
                {
                    Team team = teams[t];
                    if (team.IsEnemyOf(formation.Team))
                    {
                        AddFormationsWithUnits(team, allEnemyFormations);
                    }
                }

                if (allEnemyFormations.Count == 1)
                {
                    significantEnemy = allEnemyFormations[0];
                    return significantEnemy;
                }

                foreach (Formation enemyFormation in allEnemyFormations)
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
            List<Formation> allEnemyFormations = new List<Formation>();

            if (formation != null)
            {
                Mission.TeamCollection teams = Mission.Current.Teams;
                for (int t = 0; t < teams.Count; t++)
                {
                    Team team = teams[t];
                    if (team.IsEnemyOf(formation.Team))
                    {
                        AddFormationsWithUnits(team, allEnemyFormations);
                    }
                }

                if (allEnemyFormations.Count == 1)
                {
                    significantEnemy = allEnemyFormations[0];
                    return significantEnemy;
                }

                foreach (Formation enemyFormation in allEnemyFormations)
                {
                    if (formation != null && includeInfantry && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsInfantryFormation)
                    {
                        if (unitCountMatters)
                        {
                            significantFormations.Add(enemyFormation);
                        }
                    }
                    if (formation != null && includeRanged && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsRangedFormation)
                    {
                        if (unitCountMatters)
                        {
                            significantFormations.Add(enemyFormation);
                        }
                    }
                    if (formation != null && includeCavalry && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsCavalryFormation && !enemyFormation.QuerySystem.IsRangedCavalryFormation)
                    {
                        if (unitCountMatters)
                        {
                            significantFormations.Add(enemyFormation);
                        }
                    }
                    if (formation != null && includeHorseArchers && enemyFormation.CountOfUnits > 0 && enemyFormation.QuerySystem.IsRangedCavalryFormation)
                    {
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
                    Mission.TeamCollection teams = Mission.Current.Teams;
                    for (int t = 0; t < teams.Count; t++)
                    {
                        Team team = teams[t];
                        if (team.IsEnemyOf(formation.Team))
                        {
                            AddFormationsWithUnits(team, allEnemyFormations);
                        }
                    }

                    foreach (Formation enemyFormation in allEnemyFormations)
                    {
                        if (!enemyFormation.QuerySystem.IsCavalryFormation && !enemyFormation.QuerySystem.IsRangedCavalryFormation)
                        {
                            result = false;
                        }
                    }
                }
            }
            catch (Exception)
            {
                result = false;
            }
            return result;
        }

        // The nearest (or, unitCountMatters, the largest) formation of the asked classes in the asker's OWN team, never the asker
        // itself. Null when there is none.
        public static Formation FindSignificantAlly(Formation formation, bool includeInfantry, bool includeRanged, bool includeCavalry, bool includeMountedSkirmishers, bool includeHorseArchers, bool unitCountMatters = false)
        {
            Formation significantAlly = null;
            float dist = 10000f;
            if (formation != null && formation.Team != null)
            {
                Team team = formation.Team;
                {
                    {
                        if (unitCountMatters)
                        {
                            int unitCount = -1;
                            MBList<Formation> teamFormations = team.FormationsIncludingEmpty;
                            for (int i = 0; i < teamFormations.Count; i++)
                            {
                                Formation allyFormation = teamFormations[i];
                                if (allyFormation.CountOfUnits <= 0 || allyFormation == formation)
                                {
                                    continue;
                                }
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
                            MBList<Formation> teamFormations = team.FormationsIncludingEmpty;
                            for (int i = 0; i < teamFormations.Count; i++)
                            {
                                Formation allyFormation = teamFormations[i];
                                if (allyFormation.CountOfUnits <= 0 || allyFormation == formation)
                                {
                                    continue;
                                }
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
                TacticComponent currentTactic = TacticSelection.CurrentTacticRef(formation.Team.TeamAI);

                if (currentTactic != null && (currentTactic is RBMTacticAttackSplitInfantry || currentTactic is RBMTacticDefendSplitInfantry))
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
