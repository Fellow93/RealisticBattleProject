using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    public static partial class AgentAi
    {
        /// <summary>
        /// Banner bearers swing at distant enemies ("attacking air"): while no fighting enemy is within melee range their attack input is
        /// dropped (OnAIInputSet); targeting, facing and movement stay native. OnAIInputSet runs on the AI thread: it only reads a field
        /// OnTick (main thread) sets.
        /// </summary>
        public class BannerBearerAttackGate : AgentComponent
        {
            // m; routers do not count (a bearer cannot catch them).
            private const float FightRadius = 5f;
            // The query takes Mission's global agent-query lock, hence the interval.
            private const float CheckIntervalMin = 0.2f;
            private const float CheckIntervalMax = 0.3f;

            // Main thread only (OnTick). GetNearbyEnemyAgents clears it first.
            private static readonly MBList<Agent> _enemyScratch = new MBList<Agent>();

            // Written on the main thread, read on the AI thread.
            private volatile bool _holdAttack;

            private float _nextCheck = float.MinValue;

            public BannerBearerAttackGate(Agent agent) : base(agent)
            {
            }

            public override void OnAIInputSet(ref Agent.EventControlFlag eventFlag, ref Agent.MovementControlFlag movementFlag, ref Vec2 inputVector)
            {
                if (_holdAttack)
                {
                    movementFlag &= ~Agent.MovementControlFlag.AttackMask;
                }
            }

            public override void OnTick(float dt)
            {
                // Wielded slots are re-read each tick (the engine empties them mid-mission); a man without his banner fights.
                if (!Agent.IsActive() || !Agent.IsAIControlled || !RBMAI.Utilities.IsBannerBearer(Agent))
                {
                    _holdAttack = false;
                    return;
                }
                float now = Agent.Mission.CurrentTime;
                if (now < _nextCheck)
                {
                    return;
                }
                _nextCheck = now + MBRandom.RandomFloatRanged(CheckIntervalMin, CheckIntervalMax);
                _holdAttack = !HasFightingEnemyClose(Agent);
            }

            public static bool HasFightingEnemyClose(Agent agent)
            {
                MBList<Agent> nearby = _enemyScratch;
                Mission.Current.GetNearbyEnemyAgents(agent.Position.AsVec2, FightRadius, agent.Team, nearby);
                bool found = false;
                for (int i = 0; i < nearby.Count; i++)
                {
                    if (!nearby[i].IsRunningAway)
                    {
                        found = true;
                        break;
                    }
                }
                // Don't keep agents reachable from a static.
                nearby.Clear();
                return found;
            }

            public static bool CarriesBanner(Agent agent)
            {
                for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
                {
                    if (RBMAI.Utilities.IsBannerWeapon(agent.Equipment[i]))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        // Attached at spawn: OnAIInputSet walks the component list on the AI thread, so it is not changed mid-mission (TickPatches covers later pickups).
        [HarmonyPatch(typeof(Agent))]
        [HarmonyPatch("WieldInitialWeapons")]
        internal class BannerBearerAttackGateSpawnPatch
        {
            private static void Postfix(Agent __instance)
            {
                if (!__instance.IsHuman || __instance.Equipment == null || __instance.GetComponent<BannerBearerAttackGate>() != null
                    || !BannerBearerAttackGate.CarriesBanner(__instance))
                {
                    return;
                }
                __instance.AddComponent(new BannerBearerAttackGate(__instance));
                __instance.SetHasOnAiInputSetCallback(true);
            }
        }
    }
}
