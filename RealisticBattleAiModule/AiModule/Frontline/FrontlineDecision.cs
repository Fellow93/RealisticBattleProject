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
        }

        public static int LimitCount(int count, int max)
        {
            return MathF.Min(max, count);
        }
    }
}
