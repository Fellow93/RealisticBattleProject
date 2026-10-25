using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.MountAndBlade.Formation;
using static TaleWorlds.MountAndBlade.MovementOrder;

namespace RBMAI
{
    public static partial class Frontline
    {
        public static ConcurrentDictionary<Agent, AIDecisionState> aiDecisionCooldownDict = new ConcurrentDictionary<Agent, AIDecisionState>();

        public class AIMindset
        {
            public Timer AIDecisionTimer = null;
            public AIDecision currentDecision = AIDecision.Attack;

            public Boolean shouldClearTargetFrame = false;

            // Frames since the last decision whose SetValue updates were skipped; the next decision replays them (capped).
            public int pendingScoreUpdates = 0;
            public const int MaxPendingScoreUpdates = 240;

            // Nearest ally in the unit's lane 1-8 m ahead (set on an Attack decision), or null; the Attack branch walks up behind him.
            // Written and read by the worker ticking this agent; the AI log reads it stale-tolerantly.
            public Agent aheadAlly = null;
            // Close-up hysteresis: walking up until CloseUpGap, waiting until CloseUpResumeGap.
            public bool closingUp = false;
            // Close-up patience: wait start (float.MinValue = not waiting) and end of the current shove (user rule: push a little, not mindlessly).
            public float closeUpWaitSince = float.MinValue;
            public float closeUpShoveUntil = float.MinValue;

            public enum AIDecision
            {
                Attack,
                BackStep,
                FindAlly,
                FlankAllyLeft,
                FlankAllyRight,
                Rest
            }

            public float fallback = 0;
            public float attack = 50;
            public float findAlly = 0;
            public float flankAllyLeft = 0;
            public float flankAllyRight = 0;

            public float fallBackBase = 0;
            public float attackBase = 8;
            public float findAllyBase = 0;
            public float flankAllyLeftBase = 0;
            public float flankAllyRightBase = 0;

            public void SetValue(AIDecision decision, float value)
            {
                float changedValue = 0;
                float changedValueBase = 0;
                float changedValueFromBase = 0;
                switch (decision)
                {
                    case AIDecision.Attack:
                        {
                            changedValue = attack + value;
                            changedValueBase = attackBase;
                            changedValueFromBase = changedValue - changedValueBase;
                            break;
                        }
                    case AIDecision.BackStep:
                        {
                            changedValue = fallback + value;
                            changedValueBase = fallBackBase;
                            changedValueFromBase = changedValue - changedValueBase;
                            break;
                        }
                    case AIDecision.FindAlly:
                        {
                            changedValue = findAlly + value;
                            changedValueBase = findAllyBase;
                            changedValueFromBase = changedValue - changedValueBase;
                            break;
                        }
                    case AIDecision.FlankAllyLeft:
                        {
                            changedValue = flankAllyLeft + value;
                            changedValueBase = flankAllyLeftBase;
                            changedValueFromBase = changedValue - changedValueBase;
                            break;
                        }
                    case AIDecision.FlankAllyRight:
                        {
                            changedValue = flankAllyRight + value;
                            changedValueBase = flankAllyRightBase;
                            changedValueFromBase = changedValue - changedValueBase;
                            break;
                        }
                }
                if (changedValueFromBase > 0)
                {
                    float valueToReduce = (float)Math.Floor(Math.Sqrt(Math.Abs(changedValueFromBase)));
                    changedValue -= valueToReduce;
                }
                else
                {
                    float valueToAdd = (float)Math.Floor(Math.Sqrt(Math.Abs(changedValueFromBase)));
                    changedValue += valueToAdd;
                }
                changedValue = Math.Min(100, changedValue);
                changedValue = Math.Max(0, changedValue);

                switch (decision)
                {
                    case AIDecision.Attack:
                        {
                            attack = changedValue;
                            break;
                        }
                    case AIDecision.BackStep:
                        {
                            fallback = changedValue;
                            break;
                        }
                    case AIDecision.FindAlly:
                        {
                            findAlly = changedValue;
                            break;
                        }
                    case AIDecision.FlankAllyLeft:
                        {
                            flankAllyLeft = changedValue;
                            break;
                        }
                    case AIDecision.FlankAllyRight:
                        {
                            flankAllyRight = changedValue;
                            break;
                        }
                }
            }

            // Per agent per tick: no allocation. Strict > keeps the tie-break order Attack > BackStep > FindAlly > FlankAllyLeft > FlankAllyRight.
            public void getDecision(out AIDecision decisionType)
            {
                decisionType = AIDecision.Attack;
                float bestValue = attack;
                if (fallback > bestValue)
                {
                    decisionType = AIDecision.BackStep;
                    bestValue = fallback;
                }
                if (findAlly > bestValue)
                {
                    decisionType = AIDecision.FindAlly;
                    bestValue = findAlly;
                }
                if (flankAllyLeft > bestValue)
                {
                    decisionType = AIDecision.FlankAllyLeft;
                    bestValue = flankAllyLeft;
                }
                if (flankAllyRight > bestValue)
                {
                    decisionType = AIDecision.FlankAllyRight;
                }
            }
        }

        public class AIDecisionState
        {
            public AIMindset AIMindset = new AIMindset();

            // GetCorrectTarget walks every enemy roster (O(n^2) from the parallel job): cached per agent for a random 0.5-1 s.
            public Agent cachedTarget = null;
            public float cachedTargetExpiry = float.MinValue;
            public bool cachedTargetIsNullUnderCharge = false;

            // RBM-side stand-in for writing Agent.LastRangedHitTime: when the >50 s "archer stalled" reset fired (no engine writes from a worker).
            public float stallResetTime = float.MinValue;

            // Cavalry free-charge verdict, re-queried only on expiry (its proximity queries take Mission's global agent-query lock).
            public bool cavalryEnemyClose = false;
            public float cavalryEnemyCloseExpiry = float.MinValue;

            // ChargeWithTarget aim point (ChargeHeading); resolving it is an engine call, so reused until expiry or target change.
            public Agent cachedHeadingTarget = null;
            public WorldPosition cachedHeadingPosition = WorldPosition.Invalid;
            public float cachedHeadingExpiry = float.MinValue;

            // Step-branch memos (IsPositionOccupied / GetStepTowardNearbyAlly) avoid per-frame all-agents queries behind Mission's
            // global lock: occupied verdict reused 0.1-0.15 s within one decision, nearest ally 0.25-0.4 s. Only the worker
            // ticking this agent touches them; a stray main-thread caller (AI log) at worst costs one fresh/reused query.
            public bool occupancyVerdict = false;
            public Vec2 occupancyPosition = Vec2.Invalid;
            public AIMindset.AIDecision occupancyDecision = AIMindset.AIDecision.Attack;
            public Timer occupancyDecisionTimer = null;
            public float occupancyExpiry = float.MinValue;
            public Agent stepAllyCached = null;
            public float stepAllyRadius = 0f;
            public float stepAllyExpiry = float.MinValue;

            // Static-obstacle step gate (IsStepBlocked / IsPressingAgainstObstacle); same threading as the memos above.
            // Navmesh reachability verdict of the last probe, reused while the decision holds and he has not moved/turned much.
            public bool reachBlocked = false;
            public Vec2 reachOrigin = Vec2.Invalid;
            public Vec2 reachDirection = Vec2.Invalid;
            public AIMindset.AIDecision reachDecision = AIMindset.AIDecision.Attack;
            public float reachTime = float.MinValue;
            // Pressing detection: current run of step pins in one direction.
            public Vec2 pressAnchor = Vec2.Invalid;
            public Vec2 pressDirection = Vec2.Invalid;
            public AIMindset.AIDecision pressDecision = AIMindset.AIDecision.Attack;
            public float pressAnchorTime = float.MinValue;
            public float pressLastSeen = float.MinValue;
            public AIMindset.AIDecision pressReleaseDecision = AIMindset.AIDecision.Attack;
            public Vec2 pressReleasePosition = Vec2.Invalid;
            public float pressReleaseUntil = float.MinValue;
        }

        // This agent's melee/charge target, cached (up to ~1 s stale vs Utilities.GetCorrectTarget).
        public static Agent GetCachedCorrectTarget(Agent unit, AIDecisionState state)
        {
            if (unit == null)
            {
                return null;
            }
            if (state == null)
            {
                return Utilities.GetCorrectTarget(unit);
            }
            Mission mission = Mission.Current;
            float now = mission != null ? mission.CurrentTime : 0f;
            Agent cached = state.cachedTarget;
            if (cached != null && now < state.cachedTargetExpiry && cached.IsActive())
            {
                return cached;
            }
            // A null is reused only if found under ChargeWithTarget and the formation is still on it (GetCorrectTarget's
            // expensive path); under other orders caching null would delay target pickup after a charge is ordered.
            bool isChargeWithTarget = IsOnChargeWithTarget(unit);
            if (cached == null && state.cachedTargetIsNullUnderCharge && now < state.cachedTargetExpiry && isChargeWithTarget)
            {
                return null;
            }
            Agent target = Utilities.GetCorrectTarget(unit);
            state.cachedTarget = target;
            state.cachedTargetIsNullUnderCharge = target == null && isChargeWithTarget;
            state.cachedTargetExpiry = now + MBRandom.RandomFloatRanged(0.5f, 1f);
            return target;
        }

        private static bool IsOnChargeWithTarget(Agent unit)
        {
            Formation formation = unit.Formation;
            return formation != null && formation.GetReadonlyMovementOrderReference().OrderType == OrderType.ChargeWithTarget;
        }

        public static AIDecisionState GetOrCreateDecisionState(Agent unit)
        {
            if (unit == null)
            {
                return null;
            }
            AIDecisionState state;
            if (aiDecisionCooldownDict.TryGetValue(unit, out state))
            {
                return state;
            }
            return aiDecisionCooldownDict.GetOrAdd(unit, new AIDecisionState());
        }

        public static int LimitCount(int count, int max)
        {
            return MathF.Min(max, count);
        }
    }
}
