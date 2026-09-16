using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    // Rally: when a chunk of an AI infantry formation is far ahead of (or behind) the main body -- the
    // typical case is the surviving veterans of a first wave standing 150-250 m in front of a fresh
    // reinforcement wave -- the formation should stop advancing, hold at the main body, and let the far
    // men run to it at full speed, then carry on together.
    //
    // Why native does not do this: BehaviorRegroup's weight is driven by DeviationOfPositionsExcludeFarAgents,
    // which by construction EXCLUDES the men that matter here, so Regroup loses to Advance and the main body
    // keeps marching toward the enemy while the veterans trail into the enemy line piecemeal (logged
    // 2026-09-16 14:56: at t=228 the survivors were 200-250 m ahead of their slots, Advance kept the line
    // moving, Charge fired at t=272 with them still 120-170 m out).
    //
    // "Far" is measured along the facing direction from the MEDIAN position (median sits in the big cluster,
    // the average does not), so flank width never counts and a normal advancing block flags nobody.
    //
    // Main thread only: reads slots, swaps grid cells. Called from behavior weight/tick patches.
    internal static class RallyLogic
    {
        /// <summary>A man more than this (plus the formation depth) ahead of or behind the median is "far".</summary>
        private const float FarForwardMargin = 40f;

        /// <summary>Start a rally when at least this fraction AND this many men are far.</summary>
        private const float StartFraction = 0.06f;
        private const int StartCount = 8;

        /// <summary>Keep rallying until the far fraction drops under this (hysteresis).</summary>
        private const float EndFraction = 0.02f;

        /// <summary>No rally when the closest enemy formation's centre is within this of the main body -- fight instead.</summary>
        private const float EnemyStandoff = 45f;

        /// <summary>Regroup weight while rallying. Advance/Charge factors sit at 1-1.75, so this wins outright.</summary>
        internal const float RallyWeight = 3f;

        private const float EvalInterval = 1f;

        private const float SwapInterval = 4f;

        /// <summary>A unit-count jump of at least this many men AND this fraction counts as a reinforcement wave.</summary>
        private const int ReinforcementMinCount = 5;
        private const float ReinforcementMinFraction = 0.04f;

        /// <summary>How long after a wave lands the formation is biased toward regrouping.</summary>
        private const float ReinforcedWindow = 30f;

        /// <summary>Inside the window the rally thresholds are scaled by this (easier to trigger, slower to end).</summary>
        private const float ReinforcedThresholdScale = 0.5f;

        /// <summary>Inside the window the ordinary (non-rally) Regroup weight is multiplied by this.</summary>
        internal const float ReinforcedWeightScale = 1.5f;

        private sealed class State
        {
            public float NextEval;
            public float NextSwap;
            public bool Rallying;
            public int FarCount;
            public int TotalCount;
            public int LastTotalCount;
            public float ReinforcedUntil;
            public Vec2 MainBodyCenter;
            /// <summary>Fixed hold point for the current rally. A formation ordered onto its own live centroid
            /// pushes that centroid around (front ranks step back, the average creeps), so the point is
            /// frozen at rally start and only re-anchored if the body ends up far from it.</summary>
            public Vec2 Anchor;
        }

        /// <summary>Re-anchor the hold point if the main body has drifted this far from it.</summary>
        private const float AnchorDrift = 25f;

        private static readonly Dictionary<Formation, State> _states = new Dictionary<Formation, State>();

        private static Mission _mission;

        private static readonly List<Agent> _farScratch = new List<Agent>(64);

        private static readonly List<Agent> _nearScratch = new List<Agent>(512);

        private static State GetState(Formation formation)
        {
            if (_mission != Mission.Current)
            {
                _mission = Mission.Current;
                _states.Clear();
            }
            State state;
            if (!_states.TryGetValue(formation, out state))
            {
                state = new State();
                _states[formation] = state;
            }
            return state;
        }

        /// <summary>True while the formation should hold and gather its far men. Re-evaluated once a second.</summary>
        public static bool NeedsRally(Formation formation)
        {
            if (formation == null || Mission.Current == null || !formation.IsAIControlled)
            {
                return false;
            }
            FormationQuerySystem qs = formation.QuerySystem;
            if (qs == null || !qs.IsInfantryFormation)
            {
                return false;
            }
            State state = GetState(formation);
            float now = Mission.Current.CurrentTime;
            if (now < state.NextEval)
            {
                return state.Rallying;
            }
            state.NextEval = now + EvalInterval;
            Evaluate(formation, state);
            return state.Rallying;
        }

        /// <summary>Where the formation should hold while rallying: the anchored main-body centre.</summary>
        public static Vec2 MainBodyCenter(Formation formation)
        {
            State state = GetState(formation);
            if (state.Anchor.IsValid)
            {
                return state.Anchor;
            }
            return state.MainBodyCenter.IsValid ? state.MainBodyCenter : formation.CachedAveragePosition;
        }

        public static int FarCount(Formation formation)
        {
            State state;
            return _states.TryGetValue(formation, out state) ? state.FarCount : 0;
        }

        public static bool IsRallying(Formation formation)
        {
            State state;
            return _states.TryGetValue(formation, out state) && state.Rallying;
        }

        /// <summary>True for a while after the formation's unit count jumped (a reinforcement wave landed).</summary>
        public static bool RecentlyReinforced(Formation formation)
        {
            State state;
            return Mission.Current != null && _states.TryGetValue(formation, out state)
                && Mission.Current.CurrentTime < state.ReinforcedUntil;
        }

        private static void Evaluate(Formation formation, State state)
        {
            WorldPosition median = formation.CachedMedianPosition;
            Vec2 dir = formation.Direction;
            if (!median.IsValid || !dir.IsValid || dir.LengthSquared < 0.01f)
            {
                state.Rallying = false;
                state.Anchor = Vec2.Invalid;
                state.FarCount = 0;
                return;
            }
            Vec2 medianVec = median.AsVec2;
            float farDistance = FarForwardMargin + formation.Depth;

            int far = 0;
            int near = 0;
            Vec2 nearSum = Vec2.Zero;
            foreach (Agent agent in formation.GetUnitsWithoutDetachedOnes())
            {
                if (agent == null || !agent.IsActive() || !agent.IsHuman)
                {
                    continue;
                }
                Vec2 pos = agent.Position.AsVec2;
                float forward = (pos - medianVec).DotProduct(dir);
                if (MathF.Abs(forward) > farDistance)
                {
                    far++;
                }
                else
                {
                    near++;
                    nearSum += pos;
                }
            }
            int total = far + near;
            state.FarCount = far;
            state.TotalCount = total;
            state.MainBodyCenter = (near > 0) ? nearSum * (1f / near) : medianVec;

            // A jump in unit count is a reinforcement wave landing: open the regroup-biased window.
            float now = Mission.Current.CurrentTime;
            int previous = state.LastTotalCount;
            state.LastTotalCount = total;
            int jump = total - previous;
            if (previous > 0 && jump >= ReinforcementMinCount && jump >= previous * ReinforcementMinFraction)
            {
                state.ReinforcedUntil = now + ReinforcedWindow;
            }
            bool reinforced = now < state.ReinforcedUntil;
            float thresholdScale = reinforced ? ReinforcedThresholdScale : 1f;

            if (total == 0)
            {
                state.Rallying = false;
                state.Anchor = Vec2.Invalid;
                return;
            }

            // Enemy on the main body: fight, don't gather.
            FormationQuerySystem enemyQs = formation.CachedClosestEnemyFormation;
            if (enemyQs != null && enemyQs.Formation != null)
            {
                Vec2 enemyCenter = enemyQs.Formation.CachedAveragePosition;
                if (enemyCenter.IsValid && enemyCenter.Distance(state.MainBodyCenter) < EnemyStandoff)
                {
                    state.Rallying = false;
                    state.Anchor = Vec2.Invalid;
                    return;
                }
            }

            float fraction = far / (float)total;
            bool wasRallying = state.Rallying;
            if (state.Rallying)
            {
                state.Rallying = fraction >= EndFraction * thresholdScale && far >= 2;
            }
            else
            {
                state.Rallying = fraction >= StartFraction * thresholdScale
                    && far >= MathF.Max(2, (int)(StartCount * thresholdScale));
            }

            if (!state.Rallying)
            {
                state.Anchor = Vec2.Invalid;
            }
            else if (!wasRallying || !state.Anchor.IsValid
                || state.Anchor.Distance(state.MainBodyCenter) > AnchorDrift)
            {
                state.Anchor = state.MainBodyCenter;
            }
        }

        /// <summary>
        /// Hand every far man the cell nearest to him (swapping with the man who holds it). Otherwise a
        /// veteran 200 m in front is left with whatever far-flank cell the last re-layout gave him and runs
        /// a long diagonal. Throttled; main thread only.
        /// </summary>
        public static void PullFarMenToNearestCells(Formation formation)
        {
            if (formation == null || Mission.Current == null)
            {
                return;
            }
            State state = GetState(formation);
            float now = Mission.Current.CurrentTime;
            if (now < state.NextSwap)
            {
                return;
            }
            state.NextSwap = now + SwapInterval;

            WorldPosition median = formation.CachedMedianPosition;
            Vec2 dir = formation.Direction;
            if (!median.IsValid || !dir.IsValid)
            {
                return;
            }
            Vec2 medianVec = median.AsVec2;
            float farDistance = FarForwardMargin + formation.Depth;

            _farScratch.Clear();
            _nearScratch.Clear();
            foreach (Agent agent in formation.GetUnitsWithoutDetachedOnes())
            {
                if (agent == null || !agent.IsActive() || !agent.IsHuman || agent.IsDetachedFromFormation)
                {
                    continue;
                }
                IFormationUnit unit = agent;
                if (unit.FormationFileIndex < 0 || unit.FormationRankIndex < 0)
                {
                    continue;
                }
                float forward = (agent.Position.AsVec2 - medianVec).DotProduct(dir);
                if (MathF.Abs(forward) > farDistance)
                {
                    _farScratch.Add(agent);
                }
                else
                {
                    _nearScratch.Add(agent);
                }
            }
            if (_farScratch.Count == 0 || _nearScratch.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _farScratch.Count; i++)
            {
                Agent farMan = _farScratch[i];
                Vec2 farPos = farMan.Position.AsVec2;
                WorldPosition ownSlot;
                float best = Utilities.TryGetUnitSlot(formation, farMan, out ownSlot)
                    ? farPos.DistanceSquared(ownSlot.AsVec2) : float.MaxValue;
                int bestIndex = -1;
                for (int j = 0; j < _nearScratch.Count; j++)
                {
                    Agent nearMan = _nearScratch[j];
                    if (nearMan == null)
                    {
                        continue;
                    }
                    WorldPosition slot;
                    if (!Utilities.TryGetUnitSlot(formation, nearMan, out slot))
                    {
                        continue;
                    }
                    float d = farPos.DistanceSquared(slot.AsVec2);
                    if (d < best)
                    {
                        best = d;
                        bestIndex = j;
                    }
                }
                if (bestIndex >= 0)
                {
                    formation.SwitchUnitLocations(farMan, _nearScratch[bestIndex]);
                    _nearScratch[bestIndex] = null;
                }
            }
        }
    }
}
