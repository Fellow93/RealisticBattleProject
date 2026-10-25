using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// The hedgehog's read of each side (RBMTacticHedgehog / RBMBehaviorHedgehog), built once per field battle on the MAIN
    /// thread at the terrain publish from the agents on the field and the snapshot, published as one immutable SidePlan per
    /// side (volatile fields): men, melee infantry and polearm/shield share, MELEE cavalry (QueryLibrary.IsCavalry, the team
    /// query's split; horse archers only for the log) and power, the enemy's equivalents, its charge lanes onto us and our line
    /// clearance, and the verdict Fit (infantry minimums, a strong enemy cavalry arm, weak own cavalry, polearm/shield share).
    /// ForSide is gated on terrainTacticsEnabled (off = null = the tactic weighs 0.01); Published is not (the log). The tactic
    /// reads it on the AI thread (one volatile read of an immutable object). Behavior events go through this class's queue
    /// (AI thread writes, BattleTerrainLogic drains). Cleared at mission end and in MissionStartReset.
    /// </summary>
    internal static class TerrainHedgehog
    {

        public const float MinInfantryRatio = 0.3f;
        public const float MinInfantryCount = 40f;
        public const float HedgehogEnemyCav = 0.35f; // strong enemy cavalry arm: mounted share ...
        public const float HedgehogCavPowerRatio = 1.5f; // ... or mounted power x ours
        public const float HedgehogWeakCav = 1.25f; // our cavalry is weak: enemy mounted power x ours
        public const float HedgehogPikeShare = 0.6f; // of our melee infantry with a polearm or a shield

        private static volatile SidePlan _attacker;
        private static volatile SidePlan _defender;

        private static readonly ConcurrentQueue<UseEvent> UseEvents = new ConcurrentQueue<UseEvent>();

        /// <summary>Immutable.</summary>
        internal sealed class SidePlan
        {
            public BattleSideEnum Side;
            public int Men;
            public int Infantry;
            public int PikeOrShield;
            public float PikeShare;
            public int Mounted; // melee cavalry (QueryLibrary.IsCavalry; no horse archers)
            public float MountedPower;
            public int EnemyMen;
            public int EnemyMounted; // enemy melee cavalry (no horse archers)
            public float EnemyMountedPower;
            public float EnemyMountedShare; // of the enemy's men
            public int EnemyHorseArchers; // log only: they never make a hedgehog fit
            public float CavPowerRatio; // enemy mounted power / max(ours, 1)
            public int Lanes = -1; // enemy charge lanes onto us; -1 = not measured
            public float LaneMax = float.NaN; // m
            public float LaneMean = float.NaN;
            public float ClearLine = float.NaN; // our line's median clearance, m (SideInfo.ClearLine)
            public bool Fit;
            public string Verdict; // "fit" or the failed gates ("fewInfantry littleEnemyCav ..."), ASCII
        }

        /// <summary>The side's read, null with terrainTacticsEnabled off, before the publish or outside a field battle. Any thread.</summary>
        public static SidePlan ForSide(BattleSideEnum side)
        {
            // None for a side without terrain plans (ArmyScale).
            if (!RBMConfig.RBMConfig.terrainTacticsEnabled || ArmyScale.NoPlans(side))
            {
                return null;
            }
            return Published(side);
        }

        /// <summary>AI thread: the enemy's MELEE cavalry now (EnemyCavalryRatio x EnemyUnitCount, as SidePlan.EnemyMounted; no horse archers). 0 without a team.</summary>
        internal static float EnemyMeleeCavalryNow(Team team)
        {
            TeamQuerySystem q = team?.QuerySystem;
            return (q != null) ? q.EnemyCavalryRatio * q.EnemyUnitCount : 0f;
        }

        /// <summary>Whatever the toggle (the log).</summary>
        internal static SidePlan Published(BattleSideEnum side)
        {
            return (side == BattleSideEnum.Attacker) ? _attacker : (side == BattleSideEnum.Defender) ? _defender : null;
        }

        /// <summary>Main thread, at the terrain publish.</summary>
        internal static void Publish(SidePlan attacker, SidePlan defender)
        {
            _attacker = attacker;
            _defender = defender;
        }

        public static void Clear()
        {
            _attacker = null;
            _defender = null;
            UseEvent ignored;
            while (UseEvents.TryDequeue(out ignored))
            {
            }
        }

        // build (main thread)

        private sealed class Tally
        {
            public int Men;
            public int Infantry;
            public int PikeOrShield;
            public int Mounted;
            public float MountedPower;
            public int HorseArchers;
        }

        /// <summary>MAIN thread: the read of <paramref name="side"/> from the agents on the field and the snapshot; null outside Attacker / Defender.</summary>
        internal static SidePlan Build(Mission mission, BattleTerrain terrain, BattleSideEnum side)
        {
            if (mission == null || (side != BattleSideEnum.Attacker && side != BattleSideEnum.Defender))
            {
                return null;
            }
            Tally own = new Tally();
            Tally enemy = new Tally();
            foreach (Team team in mission.Teams)
            {
                if (team == null || (team.Side != BattleSideEnum.Attacker && team.Side != BattleSideEnum.Defender))
                {
                    continue;
                }
                Tally tally = (team.Side == side) ? own : enemy;
                foreach (Agent agent in team.ActiveAgents)
                {
                    if (agent == null || !agent.IsHuman || !agent.IsActive())
                    {
                        continue;
                    }
                    tally.Men++;
                    if (agent.HasMount)
                    {
                        // Melee cavalry only (QueryLibrary.IsCavalry); horse archers for the log.
                        if (QueryLibrary.IsCavalry(agent))
                        {
                            tally.Mounted++;
                            tally.MountedPower += agent.CharacterPowerCached;
                        }
                        else
                        {
                            tally.HorseArchers++;
                        }
                    }
                    else if (!agent.IsRangedCached)
                    {
                        tally.Infantry++;
                        if (HasPolearmOrShield(agent))
                        {
                            tally.PikeOrShield++;
                        }
                    }
                }
            }
            SidePlan plan = new SidePlan
            {
                Side = side,
                Men = own.Men,
                Infantry = own.Infantry,
                PikeOrShield = own.PikeOrShield,
                PikeShare = (own.Infantry > 0) ? (float)own.PikeOrShield / own.Infantry : 0f,
                Mounted = own.Mounted,
                MountedPower = own.MountedPower,
                EnemyMen = enemy.Men,
                EnemyMounted = enemy.Mounted,
                EnemyMountedPower = enemy.MountedPower,
                EnemyMountedShare = (enemy.Men > 0) ? (float)enemy.Mounted / enemy.Men : 0f,
                EnemyHorseArchers = enemy.HorseArchers,
                CavPowerRatio = enemy.MountedPower / System.Math.Max(own.MountedPower, 1f)
            };
            if (terrain != null)
            {
                BattleTerrain.CavalryInfo cavalry = terrain.Cavalry;
                if (cavalry != null && cavalry.Error == null)
                {
                    // The OTHER side's lanes: its riders' charge runs onto our line.
                    BattleTerrain.SideCavalry theirs = (side == BattleSideEnum.Attacker) ? cavalry.Defender : cavalry.Attacker;
                    if (theirs != null)
                    {
                        plan.Lanes = theirs.Lanes.Count;
                        float max = 0f;
                        double sum = 0.0;
                        int counted = 0;
                        foreach (BattleTerrain.ChargeLane lane in theirs.Lanes)
                        {
                            if (lane == null || float.IsNaN(lane.Length))
                            {
                                continue;
                            }
                            max = System.Math.Max(max, lane.Length);
                            sum += lane.Length;
                            counted++;
                        }
                        plan.LaneMax = (counted > 0) ? max : 0f;
                        plan.LaneMean = (counted > 0) ? (float)(sum / counted) : 0f;
                    }
                }
                BattleTerrain.SideInfo ownInfo = (side == BattleSideEnum.Attacker) ? terrain.Attacker : terrain.Defender;
                if (ownInfo != null)
                {
                    plan.ClearLine = ownInfo.ClearLine;
                }
            }
            List<string> failed = new List<string>();
            float infantryRatio = (own.Men > 0) ? (float)own.Infantry / own.Men : 0f;
            if (own.Infantry < MinInfantryCount || infantryRatio < MinInfantryRatio)
            {
                failed.Add("fewInfantry");
            }
            if (plan.EnemyMountedShare < HedgehogEnemyCav && plan.CavPowerRatio < HedgehogCavPowerRatio)
            {
                failed.Add("littleEnemyCav");
            }
            if (plan.CavPowerRatio < HedgehogWeakCav)
            {
                failed.Add("ownCavStrong");
            }
            if (plan.PikeShare < HedgehogPikeShare)
            {
                failed.Add("fewPolearms");
            }
            plan.Fit = failed.Count == 0;
            plan.Verdict = plan.Fit ? "fit" : string.Join(" ", failed);
            return plan;
        }

        /// <summary>Any weapon slot holds an item with a polearm usage or a shield.</summary>
        private static bool HasPolearmOrShield(Agent agent)
        {
            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
            {
                MissionWeapon weapon = agent.Equipment[i];
                if (weapon.IsEmpty || weapon.Item == null || weapon.Item.Weapons == null)
                {
                    continue;
                }
                foreach (WeaponComponentData usage in weapon.Item.Weapons)
                {
                    if (usage == null)
                    {
                        continue;
                    }
                    WeaponClass c = usage.WeaponClass;
                    if (c == WeaponClass.OneHandedPolearm || c == WeaponClass.TwoHandedPolearm || c == WeaponClass.LowGripPolearm
                        || c == WeaponClass.SmallShield || c == WeaponClass.LargeShield)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // behavior events (AI thread writes, main thread drains)

        /// <summary>One RBMBehaviorHedgehog change (arrangement, state, engage), for the terrain log. Immutable.</summary>
        internal sealed class UseEvent
        {
            public Team Team;
            public int FormationIndex;
            public float Time;
            public string What;
            public string Why;
        }

        /// <summary>AI thread; only with the terrain log on.</summary>
        internal static void Enqueue(Team team, Formation formation, float time, string what, string why)
        {
            DecisionTrace.Note(formation, "Hedgehog", what, why);
            if (!RBMConfig.RBMConfig.battleTerrainLoggingEnabled || team == null)
            {
                return;
            }
            UseEvents.Enqueue(new UseEvent
            {
                Team = team,
                FormationIndex = (formation != null) ? (int)formation.FormationIndex : -1,
                Time = time,
                What = what,
                Why = why
            });
        }

        /// <summary>Main thread (terrain log).</summary>
        internal static bool TryDequeueUse(out UseEvent e)
        {
            return UseEvents.TryDequeue(out e);
        }

        // log (main thread)

        /// <summary>The HEDGEHOG line's fields after the side.</summary>
        internal static string Describe(SidePlan plan)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("applied=").Append(RBMConfig.RBMConfig.terrainTacticsEnabled ? 1 : 0);
            if (plan == null)
            {
                sb.Append("\tplan=none");
                return sb.ToString();
            }
            sb.Append("\tmen=").Append(plan.Men.ToString(CultureInfo.InvariantCulture))
              .Append(" inf=").Append(plan.Infantry.ToString(CultureInfo.InvariantCulture))
              .Append(" mounted=").Append(plan.Mounted.ToString(CultureInfo.InvariantCulture));
            sb.Append("\tpolearmShield=").Append(plan.PikeOrShield.ToString(CultureInfo.InvariantCulture)).Append('/').Append(plan.Infantry.ToString(CultureInfo.InvariantCulture))
              .Append(" share=").Append(Fmt2(plan.PikeShare)).Append(" (min ").Append(Fmt2(HedgehogPikeShare)).Append(')');
            sb.Append("\tenemyCav=").Append(plan.EnemyMounted.ToString(CultureInfo.InvariantCulture)).Append('/').Append(plan.EnemyMen.ToString(CultureInfo.InvariantCulture))
              .Append(" share=").Append(Fmt2(plan.EnemyMountedShare)).Append(" (min ").Append(Fmt2(HedgehogEnemyCav)).Append(')')
              .Append(" enemyHA=").Append(plan.EnemyHorseArchers.ToString(CultureInfo.InvariantCulture)).Append("(not counted)");
            sb.Append("\tcavPower=enemy ").Append(Fmt0(plan.EnemyMountedPower)).Append(" ours ").Append(Fmt0(plan.MountedPower))
              .Append(" ratio=").Append(Fmt2(plan.CavPowerRatio)).Append(" (strong >= ").Append(Fmt2(HedgehogCavPowerRatio))
              .Append(", ours weak >= ").Append(Fmt2(HedgehogWeakCav)).Append(')');
            sb.Append("\tlanesOntoUs=");
            if (plan.Lanes < 0)
            {
                sb.Append('-');
            }
            else
            {
                sb.Append(plan.Lanes.ToString(CultureInfo.InvariantCulture)).Append(" max ").Append(Fmt0(plan.LaneMax)).Append("m mean ").Append(Fmt0(plan.LaneMean)).Append('m');
            }
            sb.Append("\tclearLine=").Append(Fmt0(plan.ClearLine)).Append('m');
            sb.Append("\tverdict=").Append(plan.Verdict ?? "-");
            return sb.ToString();
        }

        internal static string Fmt0(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0", CultureInfo.InvariantCulture);
        }

        internal static string Fmt2(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
