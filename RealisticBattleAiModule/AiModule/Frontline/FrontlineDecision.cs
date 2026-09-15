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

            // Runs per agent per tick, so it avoids allocating. The strict > comparisons keep the
            // tie-break order Attack > BackStep > FindAlly > FlankAllyLeft > FlankAllyRight, which is
            // what the previous Dictionary + Aggregate produced by walking insertion order.
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

            // Utilities.GetCorrectTarget walks every enemy formation's roster (ToArray per call), so calling
            // it per agent per tick from the parallel movement job is O(n^2) plus an allocation storm.
            // Cache it per agent for a randomised 0.5-1s so the recomputes spread across ticks instead of
            // all landing on the same one.
            public Agent cachedTarget = null;
            public float cachedTargetExpiry = float.MinValue;

            // RBM-side stand-in for the old forged write to Agent.LastRangedAttackTime: records when the
            // >50s "archer has stalled" reset fired, so the 20s/50s logic keeps its effect without
            // reflecting into engine state from a worker thread.
            public float stallResetTime = float.MinValue;
        }

        // Returns this agent's melee/charge target, reusing the cached one while it is fresh and still alive.
        // Behaviour matches a direct Utilities.GetCorrectTarget call apart from up to ~1s of staleness.
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
            Agent target = Utilities.GetCorrectTarget(unit);
            state.cachedTarget = target;
            state.cachedTargetExpiry = now + MBRandom.RandomFloatRanged(0.5f, 1f);
            return target;
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
