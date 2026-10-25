using SandBox.Missions.MissionLogics;
using System;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// Latched per-battle army scale of each side. A small side (Men &lt; SmallMen, bandit, or siege scene with field AI)
    /// gets NoPlans: no battle position, choke hold, reserve plug, hedgehog, archer spots, cavalry lanes, terrain
    /// factor or re-pick (their ForSide/PlanFor/HoldFor read null). A NoPlans side answers R1/R2 reactions only; a TINY
    /// side none, unless its power is at least the enemy's.
    /// THREADING: latched on the MAIN thread, published through volatile fields as immutable objects; any thread reads.
    /// Before the latch every side reads as normal (null). Cleared in MissionStartReset only (runs before EarlyStart, where NoteLeader sets the bandit flags).
    /// </summary>
    public static class ArmyScale
    {
        public const int SmallMen = 60; // incl. reinforcements
        public const int TinyMen = 30;

        public enum Mode
        {
            Normal,
            Small,
            Tiny
        }

        /// <summary>One side's latched scale. Immutable.</summary>
        public sealed class SideScale
        {
            public readonly BattleSideEnum Side;
            /// <summary>The whole side ("spawn": spawn logic total; "field": men on the field at the latch).</summary>
            public readonly int Men;
            public readonly int OnField;
            public readonly string Source;
            /// <summary>Sum of GetPower over the side's troops (NaN when unread); the enemy side's.</summary>
            public readonly float Power;
            public readonly float EnemyPower;
            public readonly bool Bandit;
            public readonly bool SiegeScene;
            public readonly Mode Mode;
            public readonly float LatchedAt;

            public SideScale(BattleSideEnum side, int men, int onField, string source, float power, float enemyPower, bool bandit, bool siegeScene,
                float latchedAt)
            {
                Side = side;
                Men = men;
                OnField = onField;
                Source = source;
                Power = power;
                EnemyPower = enemyPower;
                Bandit = bandit;
                SiegeScene = siegeScene;
                LatchedAt = latchedAt;
                bool strong = !float.IsNaN(power) && !float.IsNaN(enemyPower) && enemyPower > 0f && power >= enemyPower;
                Mode = (men < TinyMen && !strong) ? Mode.Tiny : (men < SmallMen) ? Mode.Small : Mode.Normal;
            }

            public bool NoPlans => Mode != Mode.Normal || Bandit || SiegeScene;

            public bool ReactionsOff => Mode == Mode.Tiny;

            /// <summary>Reactions R1/R2 only.</summary>
            public bool ReactionsLimited => NoPlans && !ReactionsOff;

            /// <summary>Why the plans are off (log text).</summary>
            public string Reason => (Mode == Mode.Tiny) ? "tiny" : (Mode == Mode.Small) ? "small" : Bandit ? "bandit" : SiegeScene ? "siegeScene" : "normal";

            public string Describe()
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("men=").Append(Men.ToString(CultureInfo.InvariantCulture))
                  .Append(" field=").Append(OnField.ToString(CultureInfo.InvariantCulture))
                  .Append(" src=").Append(Source)
                  .Append(" power=").Append(float.IsNaN(Power) ? "-" : Power.ToString("0", CultureInfo.InvariantCulture))
                  .Append("/enemy=").Append(float.IsNaN(EnemyPower) ? "-" : EnemyPower.ToString("0", CultureInfo.InvariantCulture))
                  .Append(" bandit=").Append(Bandit ? 1 : 0)
                  .Append(" siegeScene=").Append(SiegeScene ? 1 : 0)
                  .Append(" mode=").Append(Reason)
                  .Append(" plans=").Append(NoPlans ? "off" : "on")
                  .Append(" reactions=").Append(ReactionsOff ? "off" : ReactionsLimited ? "R1R2" : "all")
                  .Append(" latchedAt=").Append(LatchedAt.ToString("0.0", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        private static volatile SideScale _attacker;
        private static volatile SideScale _defender;
        private static volatile bool _attackerBandit;
        private static volatile bool _defenderBandit;

        /// <summary>The side's latched scale, null before the latch (read as normal). Any thread.</summary>
        public static SideScale For(BattleSideEnum side)
        {
            return (side == BattleSideEnum.Attacker) ? _attacker : (side == BattleSideEnum.Defender) ? _defender : null;
        }

        /// <summary>False before the latch. Any thread.</summary>
        public static bool NoPlans(BattleSideEnum side)
        {
            SideScale scale = For(side);
            return scale != null && scale.NoPlans;
        }

        public static bool NoPlans(Team team)
        {
            return team != null && NoPlans(team.Side);
        }

        public static bool Latched => _attacker != null && _defender != null;

        /// <summary>Main thread (MissionStartReset, BattleTerrainLogic.AfterStart).</summary>
        public static void Clear()
        {
            _attacker = null;
            _defender = null;
            _attackerBandit = false;
            _defenderBandit = false;
        }

        /// <summary>Main thread (Tactics.EarlyStartPatch, before any spawn): remembers whether the side's leader party is a bandit party. Campaign only; never throws.</summary>
        internal static void NoteLeader(BattleSideEnum side, IBattleCombatant leader)
        {
            bool bandit = false;
            try
            {
                PartyBase party = leader as PartyBase;
                bandit = party != null && party.MapFaction != null && party.MapFaction.IsBanditFaction;
            }
            catch
            {
                bandit = false;
            }
            if (side == BattleSideEnum.Attacker)
            {
                _attackerBandit = bandit;
            }
            else if (side == BattleSideEnum.Defender)
            {
                _defenderBandit = bandit;
            }
        }

        /// <summary>Main thread: latches both sides once the spawn logic has its totals (or, with force, from the men on the field). True when this call latched them.</summary>
        internal static bool TryLatch(Mission mission, bool force)
        {
            if (Latched || mission == null)
            {
                return false;
            }
            IBattleMissionAgentSpawnLogic spawn = mission.GetMissionBehavior<IBattleMissionAgentSpawnLogic>();
            int attackerTotal = SpawnTotal(spawn, BattleSideEnum.Attacker);
            int defenderTotal = SpawnTotal(spawn, BattleSideEnum.Defender);
            int attackerField = OnField(mission, BattleSideEnum.Attacker);
            int defenderField = OnField(mission, BattleSideEnum.Defender);
            bool fromSpawn = attackerTotal > 0 && defenderTotal > 0;
            if (!fromSpawn && !force)
            {
                return false;
            }
            float attackerPower = SidePower(mission, BattleSideEnum.Attacker);
            float defenderPower = SidePower(mission, BattleSideEnum.Defender);
            bool siegeScene = false;
            try
            {
                siegeScene = mission.GetMissionBehavior<CampaignSiegeStateHandler>() != null;
            }
            catch
            {
                siegeScene = false;
            }
            float now = mission.CurrentTime;
            string source = fromSpawn ? "spawn" : "field";
            _attacker = new SideScale(BattleSideEnum.Attacker, Math.Max(fromSpawn ? attackerTotal : 0, attackerField), attackerField, source,
                attackerPower, defenderPower, _attackerBandit, siegeScene, now);
            _defender = new SideScale(BattleSideEnum.Defender, Math.Max(fromSpawn ? defenderTotal : 0, defenderField), defenderField, source,
                defenderPower, attackerPower, _defenderBandit, siegeScene, now);
            return true;
        }

        /// <summary>The spawn logic's total for the side (all waves), -1 without one.</summary>
        private static int SpawnTotal(IBattleMissionAgentSpawnLogic spawn, BattleSideEnum side)
        {
            if (spawn == null)
            {
                return -1;
            }
            try
            {
                DefaultBattleMissionAgentSpawnLogic standard = spawn as DefaultBattleMissionAgentSpawnLogic;
                if (standard != null)
                {
                    return standard.GetTotalNumberOfTroopsForSide(side);
                }
                MissionSpawnPhase phase = (side == BattleSideEnum.Attacker) ? spawn.AttackerActivePhase : spawn.DefenderActivePhase;
                return (phase != null) ? phase.TotalSpawnNumber : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static int OnField(Mission mission, BattleSideEnum side)
        {
            int men = 0;
            foreach (Team team in mission.Teams)
            {
                if (team != null && team.Side == side)
                {
                    men += team.ActiveAgents.Count;
                }
            }
            return men;
        }

        /// <summary>Sum of GetPower over every troop of the side (spawned or not), NaN when it cannot be read.</summary>
        private static float SidePower(Mission mission, BattleSideEnum side)
        {
            try
            {
                IMissionAgentSpawnLogic spawn = mission.GetMissionBehavior<IMissionAgentSpawnLogic>();
                if (spawn == null)
                {
                    return float.NaN;
                }
                float power = 0f;
                int count = 0;
                foreach (IAgentOriginBase origin in spawn.GetAllTroopsForSide(side))
                {
                    if (origin?.Troop != null)
                    {
                        power += origin.Troop.GetPower();
                        count++;
                    }
                }
                return (count > 0) ? power : float.NaN;
            }
            catch
            {
                return float.NaN;
            }
        }
    }
}
