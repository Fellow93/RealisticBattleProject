using System.Collections.Generic;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    // Rally: when a chunk of an AI infantry formation is far ahead of (or behind) the main body -- the
    // typical case is the surviving veterans of a first wave standing 150-250 m in front of a fresh
    // reinforcement wave -- the formation should stop advancing, hold, and let the far men run to it at full
    // speed, then carry on together. Within the reinforced window it holds a little in front of the wave's
    // spawn point, toward the battle (from ReinforcementAnchor, field battles only); otherwise, or when no
    // spawn point is known, at the main body.
    //
    // Why native does not do this: BehaviorRegroup's weight is driven by DeviationOfPositionsExcludeFarAgents,
    // which by construction EXCLUDES the men that matter here, so Regroup loses to Advance and the main body
    // keeps marching toward the enemy while the veterans trail into the enemy line piecemeal (logged
    // 2026-09-16 14:56: at t=228 the survivors were 200-250 m ahead of their slots, Advance kept the line
    // moving, Charge fired at t=272 with them still 120-170 m out).
    //
    // "Far" is measured along the facing direction from the MEDIAN of the men's forward positions (the median
    // sits in the big cluster, the average does not), so flank width never counts and a normal advancing block
    // flags nobody. Not Formation.CachedMedianPosition: that is the agent nearest the AVERAGE, which with a big
    // enough remnant lands in the remnant and makes the fresh wave the "far" group.
    //
    // Far men in melee (a blow dealt or taken within EngagedSeconds) cannot come back. When enough of them are
    // fighting there is no rally -- pulling the rest back abandoned the fight and left the engaged men to die
    // alone -- and they never count toward the start/end fractions, so a rally ends when the free men arrive.
    //
    // Main thread only: reads slots, swaps grid cells. Called from behavior weight/tick patches, which all run
    // inside Formation.Tick -> FormationAI.Tick (the serial team tick), never from the parallel agent job.
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

        /// <summary>A far man who dealt or took a melee blow this recently is engaged (same test as FormationFightingInMelee).</summary>
        private const float EngagedSeconds = 6f;

        /// <summary>No rally (and a running one ends) once at least this many far men are engaged...</summary>
        private const int EngagedFarMaxCount = 4;

        /// <summary>...or at least this fraction of the far men.</summary>
        private const float EngagedFarMaxFraction = 0.25f;

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

        /// <summary>USER RULE (2026-10-03): a rally lasts at most this long (both sides). Without a cap, fresh men
        /// still spawning behind the hold point, men stuck on terrain or a few veterans tied up in melee kept the
        /// far count over the end threshold and the formation stood at its spawn anchor indefinitely.</summary>
        private const float MaxRallyDuration = 45f;

        /// <summary>After a capped rally, no new rally starts for this long, so the formation actually moves off.</summary>
        private const float RallyCooldown = 30f;

        internal sealed class State
        {
            public float NextEval;
            public float NextSwap;
            public bool Rallying;
            public int FarCount;
            public int EngagedFarCount;
            public int TotalCount;
            public int LastTotalCount;
            public float ReinforcedUntil;
            public float RallyStartedAt;
            public float NoRallyUntil;
            public Vec2 MainBodyCenter = Vec2.Invalid;

            /// <summary>The median forward position as a point (last Evaluate); PullFarMenToNearestCells measures from it.</summary>
            public Vec2 MedianPoint = Vec2.Invalid;

            /// <summary>Fixed hold point for the current rally. A formation ordered onto its own live centroid
            /// pushes that centroid around (front ranks step back, the average creeps), so the point is
            /// frozen at rally start and only re-anchored if the body ends up far from it.</summary>
            public Vec2 Anchor = Vec2.Invalid;

            /// <summary>The anchor is the latest wave's spawn point: fixed for the whole rally, never drift-re-anchored.</summary>
            public bool AnchorAtSpawn;
        }

        // NOTE: Regroup can only win while its weight factor is above zero -- FormationAI.FindBestBehavior skips a
        // zero-factor behavior before its GetAiWeight (where RallyWeight is returned) is ever called. Every field
        // tactic's advance phase therefore sets main-infantry Regroup (1.75): the TacticFullScaleAttack /
        // DefensiveLine / DefensiveEngagement patches, the RBM split tactics, RBMTacticEmbolon and the
        // TacticRangedHarrassmentOffensive override. A new tactic must do the same or its infantry never rallies.

        /// <summary>Re-anchor the hold point if the main body has drifted this far from it.</summary>
        private const float AnchorDrift = 25f;

        // Cleared per mission by MissionStartReset.
        internal static readonly Dictionary<Formation, State> states = new Dictionary<Formation, State>();

        // Per-call scratch; emptied again before PullFarMenToNearestCells returns so no Agent outlives the call.
        private static readonly List<Agent> _farScratch = new List<Agent>(64);

        private static readonly List<Agent> _nearScratch = new List<Agent>(512);

        private static readonly List<Vec2> _nearSlotScratch = new List<Vec2>(512);

        // Evaluate's per-call scratch: value types only, so nothing Agent-keyed outlives the call.
        private struct UnitSample
        {
            public Vec2 Position;
            public float Forward;
            public bool Engaged;
        }

        private static readonly List<UnitSample> _sampleScratch = new List<UnitSample>(512);

        private static readonly List<float> _forwardScratch = new List<float>(512);

        private static State GetState(Formation formation)
        {
            State state;
            if (!states.TryGetValue(formation, out state))
            {
                state = new State();
                states[formation] = state;
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

        /// <summary>Where the formation should hold while rallying: the anchored hold point (spawn point or main-body centre).</summary>
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
            return states.TryGetValue(formation, out state) ? state.FarCount : 0;
        }

        public static int EngagedFarCount(Formation formation)
        {
            State state;
            return states.TryGetValue(formation, out state) ? state.EngagedFarCount : 0;
        }

        public static bool IsRallying(Formation formation)
        {
            State state;
            return states.TryGetValue(formation, out state) && state.Rallying;
        }

        public static bool IsAnchoredAtSpawn(Formation formation)
        {
            State state;
            return states.TryGetValue(formation, out state) && state.Rallying && state.AnchorAtSpawn;
        }

        /// <summary>True for a while after the formation's unit count jumped (a reinforcement wave landed).</summary>
        public static bool RecentlyReinforced(Formation formation)
        {
            State state;
            return Mission.Current != null && states.TryGetValue(formation, out state)
                && Mission.Current.CurrentTime < state.ReinforcedUntil;
        }

        private static void Evaluate(Formation formation, State state)
        {
            Vec2 origin = formation.CachedAveragePosition;
            if (!origin.IsValid && formation.CachedMedianPosition.IsValid)
            {
                origin = formation.CachedMedianPosition.AsVec2;
            }
            Vec2 dir = formation.Direction;
            if (!origin.IsValid || !dir.IsValid || dir.LengthSquared < 0.01f)
            {
                state.Rallying = false;
                state.Anchor = Vec2.Invalid;
                state.FarCount = 0;
                state.EngagedFarCount = 0;
                state.MedianPoint = Vec2.Invalid;
                return;
            }
            float farDistance = FarForwardMargin + formation.Depth;
            // RBM's UpdateLastAttackAndHitTimesFix stamps the melee times on this clock, not Mission.CurrentTime.
            float meleeNow = MBCommon.GetTotalMissionTime();

            int far = 0;
            int engagedFar = 0;
            int near = 0;
            Vec2 nearSum = Vec2.Zero;
            Vec2 medianVec = origin;
            try
            {
                // Pass 1: each man's forward position along the facing, relative to the average (any origin
                // works, only the differences matter).
                foreach (Agent agent in formation.GetUnitsWithoutDetachedOnes())
                {
                    if (agent == null || !agent.IsActive() || !agent.IsHuman)
                    {
                        continue;
                    }
                    UnitSample sample;
                    sample.Position = agent.Position.AsVec2;
                    sample.Forward = (sample.Position - origin).DotProduct(dir);
                    sample.Engaged = meleeNow - agent.LastMeleeHitTime < EngagedSeconds
                        || meleeNow - agent.LastRecievedMeleeHitTime < EngagedSeconds;
                    _sampleScratch.Add(sample);
                    _forwardScratch.Add(sample.Forward);
                }

                // Pass 2: far = more than farDistance from the median forward position.
                float medianForward = 0f;
                if (_forwardScratch.Count > 0)
                {
                    _forwardScratch.Sort();
                    medianForward = _forwardScratch[_forwardScratch.Count / 2];
                }
                medianVec = origin + dir * medianForward;
                for (int i = 0; i < _sampleScratch.Count; i++)
                {
                    UnitSample sample = _sampleScratch[i];
                    if (MathF.Abs(sample.Forward - medianForward) > farDistance)
                    {
                        far++;
                        if (sample.Engaged)
                        {
                            engagedFar++;
                        }
                    }
                    else
                    {
                        near++;
                        nearSum += sample.Position;
                    }
                }
            }
            finally
            {
                _sampleScratch.Clear();
                _forwardScratch.Clear();
            }
            int total = far + near;
            state.FarCount = far;
            state.EngagedFarCount = engagedFar;
            state.TotalCount = total;
            state.MedianPoint = medianVec;
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
            // So does a wave of this class placed by ReinforcementAnchor (field battles), counted from when it was
            // placed. The count jump alone misses waves that land while the formation is charge-locked: this is
            // only evaluated while Regroup is weighted, so LastTotalCount is stale from before the charge and a
            // wave that refills the formation to its old size shows no jump.
            Vec2 wavePoint;
            float wavePlacedAt;
            if (AiModule.ReinforcementAnchor.TryGetRecentRallyPoint(formation.Team, formation.FormationIndex,
                    ReinforcedWindow, out wavePoint, out wavePlacedAt)
                && wavePlacedAt + ReinforcedWindow > state.ReinforcedUntil)
            {
                state.ReinforcedUntil = wavePlacedAt + ReinforcedWindow;
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

            // Engaged far men are left out of both fractions: they cannot come back, so the rally is decided by,
            // and ends with, the free men.
            int freeFar = far - engagedFar;
            int freeTotal = total - engagedFar;
            float fraction = (freeTotal > 0) ? freeFar / (float)freeTotal : 0f;
            // Enough of the far men in melee: the fight is out there, so no rally (a running one ends).
            bool farMenFighting = engagedFar > 0
                && (engagedFar >= EngagedFarMaxCount || engagedFar >= far * EngagedFarMaxFraction);
            bool wasRallying = state.Rallying;
            if (state.Rallying)
            {
                state.Rallying = !farMenFighting && fraction >= EndFraction * thresholdScale && freeFar >= 2;
            }
            else
            {
                state.Rallying = now >= state.NoRallyUntil
                    && !farMenFighting
                    && fraction >= StartFraction * thresholdScale
                    && freeFar >= MathF.Max(2, (int)(StartCount * thresholdScale));
                if (state.Rallying)
                {
                    state.RallyStartedAt = now;
                }
            }
            if (state.Rallying && wasRallying && now - state.RallyStartedAt >= MaxRallyDuration)
            {
                state.Rallying = false;
                state.NoRallyUntil = now + RallyCooldown;
            }

            if (!state.Rallying)
            {
                state.Anchor = Vec2.Invalid;
                state.AnchorAtSpawn = false;
            }
            else
            {
                if (!wasRallying || !state.Anchor.IsValid)
                {
                    state.AnchorAtSpawn = false;
                }
                // After a wave, gather where the wave came in rather than out in the field: the veterans fall
                // back onto the fresh men instead of the fresh men marching up to them. Also taken when a wave
                // lands on a rally that is already running.
                Vec2 spawnPoint;
                if (!state.AnchorAtSpawn && reinforced && ReinforcementAnchorFor(formation, out spawnPoint))
                {
                    state.Anchor = spawnPoint;
                    state.AnchorAtSpawn = true;
                }
                else if (!state.AnchorAtSpawn && (!wasRallying || !state.Anchor.IsValid
                    || state.Anchor.Distance(state.MainBodyCenter) > AnchorDrift))
                {
                    state.Anchor = state.MainBodyCenter;
                }
            }
        }

        private static bool ReinforcementAnchorFor(Formation formation, out Vec2 spawnPoint)
        {
            return AiModule.ReinforcementAnchor.TryGetRecentRallyPoint(formation.Team, formation.FormationIndex,
                ReinforcedWindow + EvalInterval, out spawnPoint);
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

            // Same "far" as Evaluate: measured from its median forward position (only ever called while rallying,
            // i.e. after an Evaluate).
            Vec2 medianVec = state.MedianPoint;
            Vec2 dir = formation.Direction;
            if (!medianVec.IsValid || !dir.IsValid)
            {
                return;
            }
            float farDistance = FarForwardMargin + formation.Depth;

            try
            {
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
                        // Near men's slots only change when one is swapped, and a swapped man is struck off
                        // below, so look each one up once instead of once per far man.
                        WorldPosition slot;
                        if (Utilities.TryGetUnitSlot(formation, agent, out slot))
                        {
                            _nearScratch.Add(agent);
                            _nearSlotScratch.Add(slot.AsVec2);
                        }
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
                        if (_nearScratch[j] == null)
                        {
                            continue;
                        }
                        float d = farPos.DistanceSquared(_nearSlotScratch[j]);
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
            finally
            {
                _farScratch.Clear();
                _nearScratch.Clear();
                _nearSlotScratch.Clear();
            }
        }
    }
}