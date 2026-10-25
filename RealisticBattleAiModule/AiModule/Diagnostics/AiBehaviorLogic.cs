using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// AI behavior log: per-second snapshot of each team's tactic and each formation's behavior/order, immediate CHANGE
    /// lines on flips, and a 5 s AGENTS/LAG pair per infantry formation. Reflection is wrapped: a moved field prints "?".
    /// </summary>
    public class AiBehaviorLogic : MissionLogic
    {
        private const float SnapshotInterval = 1f;

        // AGENTS/LAG walks every man.
        private const float AgentsInterval = 5f;

        private const int LagCount = 8;

        private bool _logging;

        private float _nextSnapshot;

        private float _nextAgents;

        private readonly Dictionary<Team, string> _lastTactic = new Dictionary<Team, string>();

        private readonly Dictionary<Formation, string> _lastBehavior = new Dictionary<Formation, string>();

        private readonly Dictionary<Formation, string> _lastOrder = new Dictionary<Formation, string>();

        private readonly HashSet<string> _reportedFailures = new HashSet<string>();

        // ---- reflection handles, resolved once (the team AI's tactic state: TacticWeightProbe) ----

        private static readonly FieldInfo _fReapplyNeeded =
            AccessTools.Field(typeof(TacticComponent), "IsTacticReapplyNeeded");

        /// <summary>_hasBattleBeenJoined is declared per concrete tactic: looked up per type, cached.</summary>
        private static readonly Dictionary<Type, FieldInfo> _fBattleJoined = new Dictionary<Type, FieldInfo>();

        public override void AfterStart()
        {
            _logging = false;
            if (!AiBehaviorLog.IsEnabled || !IsRealBattle())
            {
                return;
            }
            _logging = true;

            AiBehaviorLog.StartMission(Header());
            DecisionTrace.Start();
            _nextSnapshot = 0f;
            _nextAgents = 0f;

            try
            {
                foreach (Team team in Mission.Current.Teams)
                {
                    string selection = TacticSelection.DescribeTeam(team);
                    if (selection != null)
                    {
                        AiBehaviorLog.Write("t=" + AiBehaviorLog.Fmt(Mission.Current.CurrentTime) + "\tSELECT\t" + TeamName(team) + "\t" + selection);
                    }
                }
            }
            catch (Exception e)
            {
                ReportOnce("tacticSelection", e);
            }
        }

        public override void OnMissionTick(float dt)
        {
            if (!_logging || Mission.Current == null)
            {
                return;
            }

            float now = Mission.Current.CurrentTime;
            bool snapshot = now >= _nextSnapshot;
            bool agentsSnapshot = now >= _nextAgents;

            Team routedTeam;
            float routTime;
            float routRemaining;
            while (TacticSelection.TryDequeueRout(out routedTeam, out routTime, out routRemaining))
            {
                AiBehaviorLog.Write("t=" + AiBehaviorLog.Fmt(routTime) + "\tROUT\t" + TeamName(routedTeam)
                    + "\tremaining=" + AiBehaviorLog.Fmt(routRemaining));
            }

            try
            {
                DecisionTrace.Drain(now);
            }
            catch (Exception e)
            {
                ReportOnce("decisionTrace", e);
            }

            foreach (Team team in Mission.Current.Teams)
            {
                if (team == null || !team.HasTeamAi)
                {
                    continue;
                }

                try
                {
                    TickTeam(team, now, snapshot, agentsSnapshot);
                }
                catch (Exception e)
                {
                    ReportOnce("team", e);
                }
            }

            if (agentsSnapshot)
            {
                _nextAgents = now + AgentsInterval;
            }

            if (snapshot)
            {
                _nextSnapshot = now + SnapshotInterval;
                AiBehaviorLog.Flush();
            }
        }

        protected override void OnEndMission()
        {
            if (!_logging)
            {
                return;
            }
            try
            {
                DecisionTrace.Drain((Mission.Current != null) ? Mission.Current.CurrentTime : 0f, true);
            }
            catch (Exception e)
            {
                ReportOnce("decisionTrace", e);
            }
            DecisionTrace.Clear();
            AiBehaviorLog.EndMission("# mission ended at t=" + AiBehaviorLog.Fmt(
                (Mission.Current != null) ? Mission.Current.CurrentTime : 0f));
            _logging = false;
        }

        private void TickTeam(Team team, float now, bool snapshot, bool agentsSnapshot)
        {
            TacticComponent tactic = TacticWeightProbe.CurrentTactic(team);
            string tacticName = (tactic != null) ? tactic.GetType().Name : "-";

            string previous;
            bool tacticChanged = !_lastTactic.TryGetValue(team, out previous) || previous != tacticName;
            if (tacticChanged)
            {
                _lastTactic[team] = tacticName;
                if (previous != null)
                {
                    AiBehaviorLog.Write("t=" + AiBehaviorLog.Fmt(now) + "\tCHANGE\tTACTIC\t" + TeamName(team)
                        + "\t" + previous + " -> " + tacticName + "\tjoined=" + BattleJoined(tactic)
                        + "\toptions=" + TacticOptions(team, tactic)
                        + "\tcomp=" + TacticSelection.DescribeComposition(team));
                }
            }

            if (snapshot)
            {
                AiBehaviorLog.Write(TeamLine(team, tactic, tacticName, now));
            }

            foreach (Formation formation in team.FormationsIncludingEmpty)
            {
                if (formation == null || formation.CountOfUnits <= 0)
                {
                    continue;
                }

                try
                {
                    TickFormation(team, formation, now, snapshot, agentsSnapshot);
                }
                catch (Exception e)
                {
                    ReportOnce("formation", e);
                }
            }
        }

        private void TickFormation(Team team, Formation formation, float now, bool snapshot, bool agentsSnapshot)
        {
            string behaviorName = "-";
            if (formation.AI != null && formation.AI.ActiveBehavior != null)
            {
                behaviorName = formation.AI.ActiveBehavior.GetType().Name;
            }

            string orderName;
            try
            {
                orderName = formation.GetReadonlyMovementOrderReference().OrderEnum.ToString();
            }
            catch
            {
                orderName = "?";
            }

            string previousBehavior;
            bool firstSighting = !_lastBehavior.TryGetValue(formation, out previousBehavior);
            if (firstSighting || previousBehavior != behaviorName)
            {
                _lastBehavior[formation] = behaviorName;
                if (!firstSighting)
                {
                    AiBehaviorLog.Write("t=" + AiBehaviorLog.Fmt(now) + "\tCHANGE\tBEHAVIOR\t"
                        + TeamName(team) + "\t" + formation.FormationIndex + "\t"
                        + previousBehavior + " -> " + behaviorName);
                }
            }

            string previousOrder;
            bool firstOrder = !_lastOrder.TryGetValue(formation, out previousOrder);
            if (firstOrder || previousOrder != orderName)
            {
                _lastOrder[formation] = orderName;
                if (!firstOrder)
                {
                    AiBehaviorLog.Write("t=" + AiBehaviorLog.Fmt(now) + "\tCHANGE\tORDER\t"
                        + TeamName(team) + "\t" + formation.FormationIndex + "\t"
                        + previousOrder + " -> " + orderName);
                }
            }

            if (snapshot)
            {
                AiBehaviorLog.Write(FormationLine(team, formation, behaviorName, orderName, now));
            }

            if (agentsSnapshot && IsInfantry(formation))
            {
                try
                {
                    WriteAgentLines(team, formation, now);
                }
                catch (Exception e)
                {
                    ReportOnce("agentsSnapshot", e);
                }
            }
        }

        /// <summary>Per-agent snapshot, main thread, every 5 s (the slot lookup is a navmesh query per man).</summary>
        // AGENTS packed= counts men whose nearest formation-mate is closer than this (centre to centre).
        private const float PackedDistance = 0.7f;

        private struct AgentSample
        {
            public Agent Agent;
            public float SlotDist;      // negative = no slot could be read
            public float AheadOfSlot;   // metres in front of his current slot along the formation's facing (RUN line)
            public float AheadOfAverage; // metres in front of the men's average position along the facing
            public bool HasAhead;
            public float MaxSpeedMul;
            public float MaxSpeedLimit;
            public float FwdUnlimited;
            public float Speed;
            public float SpeedLimit;
            public bool HasMaxSpeedMul;
            public bool HasMaxSpeedLimit;
            public bool HasFwdUnlimited;
            public bool HasSpeed;
            public bool HasSpeedLimit;
            public bool Walk;
            public bool Detached;
            public bool CatchUp;
            public bool Retreating;
            public bool RunningAway;
            public bool FrameDisabled;
            public bool Alarmed;
            public bool Cautious;
            public bool SameFormation;
            public string Flags;
            public string Action;
        }

        private static bool IsInfantry(Formation formation)
        {
            try
            {
                if (formation.QuerySystem == null)
                {
                    return false;
                }
                FormationClass mainClass = formation.QuerySystem.MainClass;
                return mainClass == FormationClass.Infantry || mainClass == FormationClass.HeavyInfantry;
            }
            catch
            {
                return false;
            }
        }

        private void WriteAgentLines(Team team, Formation formation, float now)
        {
            List<AgentSample> samples = new List<AgentSample>();
            formation.ApplyActionOnEachUnit(delegate (Agent agent)
            {
                if (agent == null || !agent.IsHuman)
                {
                    return;
                }
                try
                {
                    samples.Add(Sample(formation, agent));
                }
                catch (Exception e)
                {
                    ReportOnce("agentSample", e);
                }
            });

            if (samples.Count == 0)
            {
                return;
            }

            AiBehaviorLog.Write(AgentsLine(team, formation, samples, now));
            AiBehaviorLog.Write(LagLine(team, formation, samples, now));
            string run = RunLine(team, formation, samples, now);
            if (run != null)
            {
                AiBehaviorLog.Write(run);
            }
        }

        private AgentSample Sample(Formation formation, Agent agent)
        {
            AgentSample s = new AgentSample();
            s.Agent = agent;

            s.SlotDist = -1f;
            try
            {
                WorldPosition slot;
                if (RBMAI.Utilities.TryGetUnitSlot(formation, agent, out slot))
                {
                    s.SlotDist = agent.Position.AsVec2.Distance(slot.AsVec2);
                }
            }
            catch (Exception e)
            {
                ReportOnce("slotPosition", e);
            }

            try
            {
                Vec2 direction = formation.CurrentDirection;
                if (!agent.IsDetachedFromFormation && direction.IsValid && direction.LengthSquared > 0.01f)
                {
                    Vec2 position = agent.Position.AsVec2;
                    s.AheadOfSlot = (position - formation.GetCurrentGlobalPositionOfUnit(agent, true)).DotProduct(direction);
                    s.AheadOfAverage = (position - formation.CachedAveragePosition).DotProduct(direction);
                    s.HasAhead = true;
                }
            }
            catch (Exception e)
            {
                ReportOnce("aheadOfSlot", e);
            }

            try
            {
                AgentDrivenProperties props = agent.AgentDrivenProperties;
                if (props != null)
                {
                    s.MaxSpeedMul = props.MaxSpeedMultiplier;
                    s.HasMaxSpeedMul = true;
                }
            }
            catch (Exception e)
            {
                ReportOnce("maxSpeedMultiplier", e);
            }

            try
            {
                s.MaxSpeedLimit = agent.GetMaximumSpeedLimit();
                s.HasMaxSpeedLimit = true;
            }
            catch (Exception e)
            {
                ReportOnce("getMaximumSpeedLimit", e);
            }

            try
            {
                s.FwdUnlimited = agent.GetMaximumForwardUnlimitedSpeed();
                s.HasFwdUnlimited = true;
            }
            catch (Exception e)
            {
                ReportOnce("getMaximumForwardUnlimitedSpeed", e);
            }

            try
            {
                s.Speed = agent.MovementVelocity.Length;
                s.HasSpeed = true;
            }
            catch (Exception e)
            {
                ReportOnce("movementVelocity", e);
            }

            try
            {
                s.SpeedLimit = agent.GetCurrentSpeedLimit();
                s.HasSpeedLimit = true;
            }
            catch (Exception e)
            {
                ReportOnce("getCurrentSpeedLimit", e);
            }

            try
            {
                s.Walk = agent.WalkMode;
            }
            catch (Exception e)
            {
                ReportOnce("walkMode", e);
            }

            try
            {
                s.Detached = agent.IsDetachedFromFormation;
            }
            catch (Exception e)
            {
                ReportOnce("isDetachedFromFormation", e);
            }

            try
            {
                s.CatchUp = agent.HumanAIComponent != null && agent.HumanAIComponent.ShouldCatchUpWithFormation;
            }
            catch (Exception e)
            {
                ReportOnce("shouldCatchUpWithFormation", e);
            }

            try
            {
                s.Retreating = agent.IsRetreating();
            }
            catch (Exception e)
            {
                ReportOnce("isRetreating", e);
            }

            try
            {
                s.RunningAway = agent.IsRunningAway;
            }
            catch (Exception e)
            {
                ReportOnce("isRunningAway", e);
            }

            try
            {
                // SetFormationFrameDisabled() clears IsFormationFrameEnabled; no separate getter.
                s.FrameDisabled = !agent.IsFormationFrameEnabled;
            }
            catch (Exception e)
            {
                ReportOnce("isFormationFrameEnabled", e);
            }

            try
            {
                Agent.AIStateFlag flags = agent.AIStateFlags;
                // Low two bits are an alarm state (Alarmed = 3 = Cautious|PatrollingCautious): mask first, then compare.
                Agent.AIStateFlag alarm = flags & Agent.AIStateFlag.AlarmStateMask;
                s.Alarmed = alarm == Agent.AIStateFlag.Alarmed;
                s.Cautious = alarm == Agent.AIStateFlag.Cautious || alarm == Agent.AIStateFlag.PatrollingCautious;
                s.Flags = flags.ToString();
            }
            catch (Exception e)
            {
                ReportOnce("aiStateFlags", e);
                s.Flags = "?";
            }

            try
            {
                s.Action = agent.GetCurrentActionType(0).ToString();
            }
            catch (Exception e)
            {
                ReportOnce("getCurrentActionType", e);
                s.Action = "?";
            }

            try
            {
                s.SameFormation = agent.Formation == formation;
            }
            catch (Exception e)
            {
                ReportOnce("agentFormation", e);
            }

            return s;
        }

        private string AgentsLine(Team team, Formation formation, List<AgentSample> samples, float now)
        {
            List<float> dists = new List<float>();
            int noSlot = 0;
            int detached = 0, catchUp = 0, retreating = 0, runningAway = 0;
            int walk = 0, frameDisabled = 0, alarmed = 0, cautious = 0, otherFormation = 0;
            int slowLimit = 0;

            float mulSum = 0f, mulMin = float.MaxValue;
            float fwdSum = 0f, fwdMin = float.MaxValue;
            float spdSum = 0f, limSum = 0f;
            int mulN = 0, fwdN = 0, spdN = 0, limN = 0;

            foreach (AgentSample s in samples)
            {
                if (s.SlotDist >= 0f)
                {
                    dists.Add(s.SlotDist);
                }
                else
                {
                    noSlot++;
                }

                if (s.Detached) detached++;
                if (s.CatchUp) catchUp++;
                if (s.Retreating) retreating++;
                if (s.RunningAway) runningAway++;
                if (s.Walk) walk++;
                if (s.FrameDisabled) frameDisabled++;
                if (s.Alarmed) alarmed++;
                if (s.Cautious) cautious++;
                if (!s.SameFormation) otherFormation++;

                if (s.HasMaxSpeedLimit)
                {
                    if (s.MaxSpeedLimit < 1f) slowLimit++;
                }
                if (s.HasMaxSpeedMul)
                {
                    mulSum += s.MaxSpeedMul;
                    if (s.MaxSpeedMul < mulMin) mulMin = s.MaxSpeedMul;
                    mulN++;
                }
                if (s.HasFwdUnlimited)
                {
                    fwdSum += s.FwdUnlimited;
                    if (s.FwdUnlimited < fwdMin) fwdMin = s.FwdUnlimited;
                    fwdN++;
                }
                if (s.HasSpeed) { spdSum += s.Speed; spdN++; }
                if (s.HasSpeedLimit) { limSum += s.SpeedLimit; limN++; }
            }

            dists.Sort();

            // Frontline decision is written on movement workers; a stale enum read is fine for a log.
            int[] flDecisions = new int[6];
            int flNone = 0;
            int flCloseUp = 0;
            List<Vec2> positions = new List<Vec2>(samples.Count);
            foreach (AgentSample s in samples)
            {
                try
                {
                    if (s.Agent.IsActive())
                    {
                        positions.Add(s.Agent.Position.AsVec2);
                    }
                    if (Frontline.aiDecisionCooldownDict.TryGetValue(s.Agent, out Frontline.AIDecisionState state) && state != null)
                    {
                        int d = (int)state.AIMindset.currentDecision;
                        if (d >= 0 && d < flDecisions.Length)
                        {
                            flDecisions[d]++;
                        }
                        if (state.AIMindset.currentDecision == Frontline.AIMindset.AIDecision.Attack && state.AIMindset.aheadAlly != null)
                        {
                            flCloseUp++;
                        }
                    }
                    else
                    {
                        flNone++;
                    }
                }
                catch (Exception e)
                {
                    ReportOnce("frontlineDecision", e);
                }
            }
            List<float> nearest = new List<float>(positions.Count);
            int packed = 0;
            for (int i = 0; i < positions.Count; i++)
            {
                float best = float.MaxValue;
                for (int j = 0; j < positions.Count; j++)
                {
                    if (i != j)
                    {
                        float d2 = positions[i].DistanceSquared(positions[j]);
                        if (d2 < best)
                        {
                            best = d2;
                        }
                    }
                }
                if (best < float.MaxValue)
                {
                    float d = (float)Math.Sqrt(best);
                    nearest.Add(d);
                    if (d < PackedDistance)
                    {
                        packed++;
                    }
                }
            }
            nearest.Sort();

            StringBuilder sb = new StringBuilder();
            sb.Append("t=").Append(AiBehaviorLog.Fmt(now)).Append("\tAGENTS");
            sb.Append('\t').Append(TeamName(team));
            sb.Append('\t').Append(formation.FormationIndex);
            sb.Append('\t').Append(samples.Count);
            sb.Append('\t').Append("slotMean=").Append(Mean(dists));
            sb.Append('\t').Append("slotP50=").Append(Percentile(dists, 0.5f));
            sb.Append('\t').Append("slotP90=").Append(Percentile(dists, 0.9f));
            sb.Append('\t').Append("slotMax=").Append((dists.Count > 0) ? AiBehaviorLog.Fmt(dists[dists.Count - 1]) : "-");
            sb.Append('\t').Append("noSlot=").Append(noSlot);
            sb.Append('\t').Append("detached=").Append(detached);
            sb.Append('\t').Append("catchUp=").Append(catchUp);
            // Worker-thread postfix counters; both 0 = patch not live.
            sb.Append('\t').Append("pfxCalls=").Append(OverrideParallelFormationMovement.CallCount);
            sb.Append('\t').Append("pfxSet=").Append(OverrideParallelFormationMovement.SetCount);
            sb.Append('\t').Append("devCaps=").Append(FormationCatchUpGate.CapCount);
            sb.Append('\t').Append("flUnreach=").Append(System.Threading.Interlocked.Read(ref Frontline.ObstacleUnreachableRejects));
            sb.Append('\t').Append("flDeep=").Append(System.Threading.Interlocked.Read(ref Frontline.ObstacleDeepWaterRejects));
            sb.Append('\t').Append("flProbes=").Append(System.Threading.Interlocked.Read(ref Frontline.ObstacleProbeQueries));
            sb.Append('\t').Append("flPress=").Append(System.Threading.Interlocked.Read(ref Frontline.ObstaclePressReleases));
            sb.Append('\t').Append("flDec=A").Append(flDecisions[(int)Frontline.AIMindset.AIDecision.Attack])
              .Append("/B").Append(flDecisions[(int)Frontline.AIMindset.AIDecision.BackStep])
              .Append("/F").Append(flDecisions[(int)Frontline.AIMindset.AIDecision.FindAlly])
              .Append("/L").Append(flDecisions[(int)Frontline.AIMindset.AIDecision.FlankAllyLeft])
              .Append("/R").Append(flDecisions[(int)Frontline.AIMindset.AIDecision.FlankAllyRight])
              .Append("/rest").Append(flDecisions[(int)Frontline.AIMindset.AIDecision.Rest])
              .Append("/none").Append(flNone)
              .Append("/closeUp").Append(flCloseUp);
            sb.Append('\t').Append("nnMean=").Append(Mean(nearest));
            sb.Append('\t').Append("nnP10=").Append(Percentile(nearest, 0.1f));
            sb.Append('\t').Append("nnP50=").Append(Percentile(nearest, 0.5f));
            sb.Append('\t').Append("packed=").Append(packed);
            sb.Append('\t').Append("rangedArr=").Append(RBMAI.Utilities.DescribeRangedArrangementChanges());            sb.Append('\t').Append("retreating=").Append(retreating);
            sb.Append('\t').Append("runAway=").Append(runningAway);
            sb.Append('\t').Append("walk=").Append(walk);
            sb.Append('\t').Append("frameDis=").Append(frameDisabled);
            sb.Append('\t').Append("alarmed=").Append(alarmed);
            sb.Append('\t').Append("cautious=").Append(cautious);
            sb.Append('\t').Append("otherForm=").Append(otherFormation);
            sb.Append('\t').Append("maxSpdLimLt1=").Append(slowLimit);
            sb.Append('\t').Append("mulMean=").Append(Avg(mulSum, mulN));
            sb.Append('\t').Append("mulMin=").Append((mulN > 0) ? AiBehaviorLog.Fmt(mulMin) : "-");
            sb.Append('\t').Append("fwdMean=").Append(Avg(fwdSum, fwdN));
            sb.Append('\t').Append("fwdMin=").Append((fwdN > 0) ? AiBehaviorLog.Fmt(fwdMin) : "-");
            sb.Append('\t').Append("velMean=").Append(Avg(spdSum, spdN));
            sb.Append('\t').Append("spdLimMean=").Append(Avg(limSum, limN));
            return sb.ToString();
        }

        private string LagLine(Team team, Formation formation, List<AgentSample> samples, float now)
        {
            List<AgentSample> worst = new List<AgentSample>(samples);
            worst.Sort((a, b) => b.SlotDist.CompareTo(a.SlotDist));

            StringBuilder sb = new StringBuilder();
            sb.Append("t=").Append(AiBehaviorLog.Fmt(now)).Append("\tLAG");
            sb.Append('\t').Append(TeamName(team));
            sb.Append('\t').Append(formation.FormationIndex);

            for (int i = 0; i < worst.Count && i < LagCount; i++)
            {
                AgentSample s = worst[i];
                sb.Append('\t').Append(AgentId(s.Agent));
                sb.Append('|').Append((s.SlotDist >= 0f) ? AiBehaviorLog.Fmt(s.SlotDist) : "-");
                sb.Append('|').Append(s.HasMaxSpeedMul ? AiBehaviorLog.Fmt(s.MaxSpeedMul) : "-");
                sb.Append('|').Append(s.HasMaxSpeedLimit ? AiBehaviorLog.Fmt(s.MaxSpeedLimit) : "-");
                sb.Append('|').Append(s.HasFwdUnlimited ? AiBehaviorLog.Fmt(s.FwdUnlimited) : "-");
                sb.Append('|').Append("w").Append(s.Walk ? "1" : "0");
                sb.Append('|').Append("v").Append(s.HasSpeed ? AiBehaviorLog.Fmt(s.Speed) : "-");
                sb.Append('|').Append("det").Append(s.Detached ? "1" : "0");
                sb.Append('|').Append("catch").Append(s.CatchUp ? "1" : "0");
                sb.Append('|').Append("fd").Append(s.FrameDisabled ? "1" : "0");
                sb.Append('|').Append(s.Flags ?? "-");
                sb.Append('|').Append(s.Action ?? "-");
                sb.Append('|').Append("sameForm").Append(s.SameFormation ? "1" : "0");
            }
            return sb.ToString();
        }

        private string RunLine(Team team, Formation formation, List<AgentSample> samples, float now)
        {
            List<AgentSample> ahead = samples.FindAll(s => s.HasAhead && s.AheadOfSlot > RunAheadMin);
            if (ahead.Count == 0)
            {
                return null;
            }
            ahead.Sort((a, b) => b.AheadOfSlot.CompareTo(a.AheadOfSlot));

            StringBuilder sb = new StringBuilder();
            sb.Append("t=").Append(AiBehaviorLog.Fmt(now)).Append("\tRUN");
            sb.Append('\t').Append(TeamName(team));
            sb.Append('\t').Append(formation.FormationIndex);
            sb.Append('\t').Append("count=").Append(ahead.Count);
            for (int i = 0; i < ahead.Count && i < LagCount; i++)
            {
                AgentSample s = ahead[i];
                sb.Append('\t').Append(AgentId(s.Agent));
                sb.Append('|').Append("slot+").Append(AiBehaviorLog.Fmt(s.AheadOfSlot));
                sb.Append('|').Append("avg+").Append(AiBehaviorLog.Fmt(s.AheadOfAverage));
                sb.Append('|').Append("lim").Append(s.HasMaxSpeedLimit ? AiBehaviorLog.Fmt(s.MaxSpeedLimit) : "-");
                sb.Append('|').Append("v").Append(s.HasSpeed ? AiBehaviorLog.Fmt(s.Speed) : "-");
                sb.Append('|').Append("catch").Append(s.CatchUp ? "1" : "0");
                sb.Append('|').Append(s.Action ?? "-");
            }
            return sb.ToString();
        }

        private const float RunAheadMin = 3f;

        private string AgentId(Agent agent)
        {
            try
            {
                if (agent == null)
                {
                    return "-";
                }
                string id = (agent.Character != null) ? agent.Character.StringId : "?";
                return id + "#" + agent.Index;
            }
            catch (Exception e)
            {
                ReportOnce("agentId", e);
                return "?";
            }
        }

        private static string Avg(float sum, int count)
        {
            return (count > 0) ? AiBehaviorLog.Fmt(sum / count) : "-";
        }

        private static string Mean(List<float> sorted)
        {
            if (sorted.Count == 0)
            {
                return "-";
            }
            float sum = 0f;
            for (int i = 0; i < sorted.Count; i++)
            {
                sum += sorted[i];
            }
            return AiBehaviorLog.Fmt(sum / sorted.Count);
        }

        /// <summary>Nearest-rank percentile on a sorted list.</summary>
        private static string Percentile(List<float> sorted, float q)
        {
            if (sorted.Count == 0)
            {
                return "-";
            }
            int index = (int)(q * (sorted.Count - 1) + 0.5f);
            if (index < 0) index = 0;
            if (index >= sorted.Count) index = sorted.Count - 1;
            return AiBehaviorLog.Fmt(sorted[index]);
        }

        private string TeamLine(Team team, TacticComponent tactic, string tacticName, float now)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("t=").Append(AiBehaviorLog.Fmt(now)).Append("\tTEAM");
            sb.Append('\t').Append(TeamName(team));
            sb.Append('\t').Append(team.Side);
            sb.Append('\t').Append(Flag("playerTeam", team.IsPlayerTeam));
            sb.Append('\t').Append(Flag("playerGeneral", team.IsPlayerGeneral));
            sb.Append('\t').Append(Flag("playerSergeant", team.IsPlayerSergeant));
            sb.Append('\t').Append(Flag("playerAlly", team.IsPlayerAlly));
            sb.Append('\t').Append((team.TeamAI != null) ? team.TeamAI.GetType().Name : "-");
            sb.Append('\t').Append(tacticName);
            sb.Append('\t').Append("joined=").Append(BattleJoined(tactic));
            sb.Append('\t').Append("reapply=").Append(ReapplyNeeded(tactic));
            sb.Append('\t').Append("aiForms=").Append(SafeInt(team.GetAIControlledFormationCount));
            sb.Append('\t').Append(Flag("allowAiTicking", Mission.Current.AllowAiTicking));
            sb.Append('\t').Append("options=").Append(TacticOptions(team, tactic));
            sb.Append('\t').Append("comp=").Append(TacticSelection.DescribeComposition(team));
            return sb.ToString();
        }

        /// <summary>Every tactic the team may pick with its weight, the active one starred; (xM tF) = TacticSelection multiplier and terrain factor when not 1.</summary>
        private string TacticOptions(Team team, TacticComponent active)
        {
            try
            {
                List<KeyValuePair<TacticComponent, float>> weights = TacticWeightProbe.Evaluate(team);
                if (weights == null || weights.Count == 0)
                {
                    return "-";
                }
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<TacticComponent, float> entry in weights)
                {
                    TacticComponent option = entry.Key;
                    if (sb.Length > 0)
                    {
                        sb.Append(' ');
                    }
                    if (option == active)
                    {
                        sb.Append('*');
                    }
                    sb.Append(option.GetType().Name).Append(':');
                    sb.Append(AiBehaviorLog.Fmt(entry.Value));
                    float multiplier;
                    if (TacticSelection.TryGetMultiplier(option, out multiplier))
                    {
                        sb.Append("(x").Append(AiBehaviorLog.Fmt(multiplier));
                        float terrain = TacticSelection.TerrainFactorFor(option);
                        if (terrain != 1f)
                        {
                            sb.Append(" t").Append(AiBehaviorLog.Fmt(terrain));
                        }
                        sb.Append(')');
                    }
                }
                return sb.ToString();
            }
            catch (Exception e)
            {
                ReportOnce("tacticOptions", e);
                return "?";
            }
        }

        private string FormationLine(Team team, Formation formation, string behaviorName, string orderName, float now)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("t=").Append(AiBehaviorLog.Fmt(now)).Append("\tFORM");
            sb.Append('\t').Append(TeamName(team));
            sb.Append('\t').Append(formation.FormationIndex);
            sb.Append('\t').Append(Safe(() =>
                (formation.QuerySystem != null) ? formation.QuerySystem.MainClass.ToString() : "-"));
            sb.Append('\t').Append(formation.CountOfUnits);
            sb.Append('\t').Append(Flag("aiControlled", formation.IsAIControlled));
            sb.Append('\t').Append(Flag("playerIn", formation.IsPlayerTroopInFormation));
            sb.Append('\t').Append(Flag("playerLed", formation.HasPlayerControlledTroop));
            sb.Append('\t').Append(behaviorName);
            sb.Append('\t').Append("side=").Append((formation.AI != null) ? formation.AI.Side.ToString() : "-");
            sb.Append('\t').Append(orderName);
            sb.Append('\t').Append("state=").Append(Safe(() => formation.GetMovementState().ToString()));
            sb.Append('\t').Append("arr=").Append(Safe(() =>
                (formation.ArrangementOrder.OrderEnum).ToString()));
            sb.Append('\t').Append("form=").Append(Safe(() => formation.FormOrder.OrderEnum.ToString()));
            sb.Append('\t').Append("w=").Append(SafeFloat(() => formation.Width));
            sb.Append('\t').Append("d=").Append(SafeFloat(() => formation.Depth));
            sb.Append('\t').Append("rally=").Append(Safe(() =>
                (RallyLogic.IsRallying(formation) ? "1" : "0") + "/" + RallyLogic.FarCount(formation)
                + "/" + RallyLogic.EngagedFarCount(formation)
                + (RallyLogic.RecentlyReinforced(formation) ? "/reinf" : "")
                + (RallyLogic.IsAnchoredAtSpawn(formation) ? "/spawn" : "")));
            sb.Append('\t').Append("spd=").Append(SafeFloat(() => formation.CachedMovementSpeed));
            sb.Append('\t').Append("spdMax=").Append(SafeFloat(() =>
                (formation.QuerySystem != null) ? formation.QuerySystem.MovementSpeedMaximum : 0f));
            sb.Append('\t').Append("dev=").Append(SafeFloat(() =>
                formation.CachedFormationIntegrityData.DeviationOfPositionsExcludeFarAgents));
            sb.Append('\t').Append("idealDisp=").Append(SafeFloat(() =>
                (formation.QuerySystem != null) ? formation.QuerySystem.IdealAverageDisplacement : 0f));

            Vec2 avg = formation.CachedAveragePosition;
            sb.Append('\t').Append("avg=").Append(Pos(avg));

            bool orderValid = false;
            Vec2 order = Vec2.Zero;
            try
            {
                orderValid = formation.OrderPositionIsValid;
                if (orderValid)
                {
                    order = formation.OrderPosition;
                }
            }
            catch
            {
                orderValid = false;
            }
            sb.Append('\t').Append("order=").Append(orderValid ? Pos(order) : "-");
            sb.Append('\t').Append("distToOrder=").Append(orderValid
                ? AiBehaviorLog.Fmt(avg.Distance(order))
                : "-");
            sb.Append('\t').Append("distToEnemy=").Append(SafeFloat(() =>
            {
                float sq = formation.CachedClosestEnemyFormationDistanceSquared;
                return (sq > 0f) ? (float)Math.Sqrt(sq) : 0f;
            }));
            sb.Append('\t').Append("dir=").Append(Safe(() =>
            {
                Vec2 dir = formation.Direction;
                return dir.IsValid ? AiBehaviorLog.Fmt(dir.x) + "," + AiBehaviorLog.Fmt(dir.y) : "-";
            }));
            sb.Append('\t').Append("weights=").Append(BehaviorWeights(formation));
            return sb.ToString();
        }

        /// <summary>The three highest-bidding behaviors inside the formation.</summary>
        private string BehaviorWeights(Formation formation)
        {
            if (formation.AI == null)
            {
                return "-";
            }

            List<KeyValuePair<string, float>> scored = new List<KeyValuePair<string, float>>();
            try
            {
                int count = formation.AI.BehaviorCount;
                for (int i = 0; i < count; i++)
                {
                    BehaviorComponent behavior = formation.AI.GetBehaviorAtIndex(i);
                    if (behavior == null)
                    {
                        continue;
                    }
                    float weightFactor = behavior.WeightFactor;
                    float aiWeight;
                    try
                    {
                        aiWeight = behavior.GetAIWeight();
                    }
                    catch
                    {
                        aiWeight = float.NaN;
                    }
                    scored.Add(new KeyValuePair<string, float>(
                        behavior.GetType().Name + ":" + AiBehaviorLog.Fmt(aiWeight)
                        + "*" + AiBehaviorLog.Fmt(weightFactor)
                        + "/c" + AiBehaviorLog.Fmt(behavior.BehaviorCoherence),
                        aiWeight * weightFactor));
                }
            }
            catch (Exception e)
            {
                ReportOnce("behaviorWeights", e);
                return "?";
            }

            scored.Sort((a, b) => b.Value.CompareTo(a.Value));
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < scored.Count && i < 3; i++)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                }
                sb.Append(scored[i].Key);
            }
            return (sb.Length > 0) ? sb.ToString() : "-";
        }

        private string BattleJoined(TacticComponent tactic)
        {
            if (tactic == null)
            {
                return "-";
            }
            try
            {
                Type type = tactic.GetType();
                FieldInfo field;
                if (!_fBattleJoined.TryGetValue(type, out field))
                {
                    field = null;
                    for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
                    {
                        field = AccessTools.DeclaredField(t, "_hasBattleBeenJoined");
                        if (field != null)
                        {
                            break;
                        }
                    }
                    _fBattleJoined[type] = field;
                }
                if (field == null)
                {
                    return "n/a";
                }
                return ((bool)field.GetValue(tactic)) ? "1" : "0";
            }
            catch (Exception e)
            {
                ReportOnce("hasBattleBeenJoined", e);
                return "?";
            }
        }

        private string ReapplyNeeded(TacticComponent tactic)
        {
            if (tactic == null || _fReapplyNeeded == null)
            {
                return "-";
            }
            try
            {
                return ((bool)_fReapplyNeeded.GetValue(tactic)) ? "1" : "0";
            }
            catch (Exception e)
            {
                ReportOnce("isTacticReapplyNeeded", e);
                return "?";
            }
        }

        private static string TeamName(Team team)
        {
            return team.Side.ToString() + "#" + team.TeamIndex;
        }

        private static string Flag(string name, bool value)
        {
            return name + "=" + (value ? "1" : "0");
        }

        private static string Pos(Vec2 p)
        {
            return AiBehaviorLog.Fmt(p.x) + "," + AiBehaviorLog.Fmt(p.y);
        }

        private string Safe(Func<string> read)
        {
            try
            {
                return read();
            }
            catch (Exception e)
            {
                ReportOnce("read", e);
                return "?";
            }
        }

        private string SafeFloat(Func<float> read)
        {
            try
            {
                return AiBehaviorLog.Fmt(read());
            }
            catch (Exception e)
            {
                ReportOnce("readFloat", e);
                return "?";
            }
        }

        private string SafeInt(Func<int> read)
        {
            try
            {
                return read().ToString();
            }
            catch (Exception e)
            {
                ReportOnce("readInt", e);
                return "?";
            }
        }

        private void ReportOnce(string key, Exception e)
        {
            if (_reportedFailures.Contains(key))
            {
                return;
            }
            _reportedFailures.Add(key);
            AiBehaviorLog.Write("# FAILED [" + key + "] " + e.GetType().Name + ": " + e.Message);
        }

        private static bool IsRealBattle()
        {
            Mission m = Mission.Current;
            if (m == null)
            {
                return false;
            }
            return m.IsFieldBattle || m.IsSiegeBattle || m.IsSallyOutBattle || m.IsNavalBattle;
        }

        private static string Header()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("# RBM AI behavior log -- what each team's AI decided, and what each formation did about it.").Append("\n");
            try
            {
                string dll = typeof(AiBehaviorLogic).Assembly.Location;
                sb.Append("# RBMAI.dll built ").Append(System.IO.File.GetLastWriteTime(dll).ToString("yyyy-MM-dd HH:mm:ss"))
                  .Append("  (log opened ").Append(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(")").Append("\n");
            }
            catch { }
            try
            {
                sb.Append("# IsFormationReshufflingUnsafe=").Append(RBMAI.Tactics.IsFormationReshufflingUnsafe).Append("\n");
                foreach (string name in new[] { "ParallelUpdateFormationMovement", "GetFormationFrame", "GetDesiredSpeedInFormation" })
                {
                    var m = HarmonyLib.AccessTools.Method(typeof(HumanAIComponent), name);
                    sb.Append("# patch ").Append(name).Append(": ");
                    if (m == null) { sb.Append("METHOD NOT FOUND\n"); continue; }
                    var info = HarmonyLib.Harmony.GetPatchInfo(m);
                    if (info == null) { sb.Append("no patches\n"); continue; }
                    sb.Append("prefixes=").Append(string.Join(",", info.Prefixes.Select(p => p.owner + ":" + p.PatchMethod.DeclaringType?.Name)))
                      .Append(" postfixes=").Append(string.Join(",", info.Postfixes.Select(p => p.owner + ":" + p.PatchMethod.DeclaringType?.Name)))
                      .Append("\n");
                }
            }
            catch (System.Exception e) { sb.Append("# patch-info FAILED ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append("\n"); }
            sb.Append("#").Append("\n");
            sb.Append("# Written to answer one question: why the enemy team's infantry behaves differently from the").Append("\n");
            sb.Append("# player's delegated team. Same code, different inputs -- and the inputs are all here.").Append("\n");
            sb.Append("#").Append("\n");
            sb.Append("# Tab separated. Three line kinds:").Append("\n");
            sb.Append("#").Append("\n");
            sb.Append("#   TEAM   t  TEAM  team  side  playerTeam  playerGeneral  playerSergeant  playerAlly").Append("\n");
            sb.Append("#          teamAIType  tacticType  joined  reapply  aiForms  allowAiTicking  options  comp").Append("\n");
            sb.Append("#             joined  = the tactic's own _hasBattleBeenJoined (n/a if that tactic has none);").Append("\n");
            sb.Append("#                       0 = advance phase, 1 = attack phase").Append("\n");
            sb.Append("#             options = every tactic the team may pick as Name:GetTacticWeight, active one starred").Append("\n");
            sb.Append("#                       (a switch needs another option to beat the active one x1.5); (xM) = the").Append("\n");
            sb.Append("#                       TacticSelection multiplier already inside that weight; (xM tF) = also the").Append("\n");
            sb.Append("#                       terrain factor F inside it (terrainTacticsEnabled, after the terrain publish)").Append("\n");
            sb.Append("#             reapply = TacticComponent.IsTacticReapplyNeeded").Append("\n");
            sb.Append("#             aiForms = Team.GetAIControlledFormationCount()").Append("\n");
            sb.Append("#             comp    = what the RBM tactics grade (TacticSelection.DescribeComposition): men own/enemy,").Append("\n");
            sb.Append("#                       inf share(own vs enemy infantry), splitInf = superiority t 0..1 or unfit,").Append("\n");
            sb.Append("#                       rng share(count), splitArch = archer fit 0..1 or unfit, cav, ha = horse-archer").Append("\n");
            sb.Append("#                       share, wedgeRiders/barded = the wedge's last count, rem = remaining power,").Append("\n");
            sb.Append("#                       defApplicable (defenders) = native gate for the defensive tactics (0 = they weigh 0)").Append("\n");
            sb.Append("#                       CHANGE TACTIC lines carry the same column.").Append("\n");
            sb.Append("#").Append("\n");
            sb.Append("#   ROUT   t  ROUT  team  remaining  -- a side under 10% power with nobody on foot was routed at once").Append("\n");
            sb.Append("#          (TacticSelection's MakeDecision prefix); once per team.").Append("\n");
            sb.Append("#").Append("\n");
            sb.Append("#   (The battlefield terrain read -- TERRAIN and TERRAIN_* -- and the log-only terrain tactic prediction --").Append("\n");
            sb.Append("#   PREDICT, TACTIC, TACTICSUMMARY -- are their own log: logs/terrain, BattleTerrainLogic.)").Append("\n");
            sb.Append("#").Append("\n");
            sb.Append("#   SELECT t  SELECT  team  culture  commander  valor  calculating  tactics  options").Append("\n");
            sb.Append("#          once at battle start, field battles: the side's culture and commander (traits -2..2,").Append("\n");
            sb.Append("#          Tactics skill), then per option Name:c<culture>/v<valor>/k<calculating>/s<skill>/j<jitter>=x<total>").Append("\n");
            sb.Append("#").Append("\n");
            sb.Append("#   FORM   t  FORM  team  formationIndex  primaryClass  units  aiControlled  playerIn").Append("\n");
            sb.Append("#          playerLed  activeBehavior  side  movementOrder  state  arr  form").Append("\n");
            sb.Append("#          w  d  rally  spd  spdMax  dev  idealDisp  avg  order  distToOrder  distToEnemy  dir  weights").Append("\n");
            sb.Append("#             state       = Formation.GetMovementState()").Append("\n");
            sb.Append("#             arr/form    = ArrangementOrder / FormOrder enum").Append("\n");
            sb.Append("#             rally       = <1 while RallyLogic holds the formation for its far men>/<far men>/<far men in").Append("\n");
            sb.Append("#                           melee, which block the rally at 4 or 25% of the far men>[/reinf in the").Append("\n");
            sb.Append("#                           30s after a reinforcement wave][/spawn when holding at the wave's spawn point]").Append("\n");            sb.Append("#             spd/spdMax  = CachedMovementSpeed / QuerySystem.MovementSpeedMaximum").Append("\n");
            sb.Append("#             dev         = DeviationOfPositionsExcludeFarAgents (how ragged the line is)").Append("\n");
            sb.Append("#             idealDisp   = QuerySystem.IdealAverageDisplacement").Append("\n");
            sb.Append("#             avg/order   = CachedAveragePosition / OrderPosition, as x,y").Append("\n");
            sb.Append("#             distToEnemy = sqrt(CachedClosestEnemyFormationDistanceSquared)").Append("\n");
            sb.Append("#             dir         = Formation.Direction (the facing), as x,y").Append("\n");
            sb.Append("#             weights     = top 3 behaviors as Name:aiWeight*weightFactor/cCoherence").Append("\n");
            sb.Append("#").Append("\n");
            sb.Append("#   CHANGE t  CHANGE  TACTIC|BEHAVIOR|ORDER  team  [formation]  old -> new").Append("\n");
            sb.Append("#          TACTIC also carries the new tactic's joined flag and the options/comp columns.").Append("\n");
            sb.Append("#          written the frame it happens, not on the next snapshot.").Append("\n");
            sb.Append("#").Append("\n");
            sb.Append("#   AGENTS t  AGENTS  team  formationIndex  units  then the columns below.").Append("\n");
            sb.Append("#          Infantry formations only. Written because the formation-level columns showed both").Append("\n");
            sb.Append("#          teams on the same tactic, behavior, speed and order distance while one line stayed").Append("\n");
            sb.Append("#          ragged (dev ~15m) and the other closed up (dev ~3m). If the orders match, the").Append("\n");
            sb.Append("#          difference is in the men, so every man is measured against his own slot.").Append("\n");
            sb.Append("#             slotMean/P50/P90/Max = |agent.Position - Formation.GetOrderPositionOfUnit(agent)|").Append("\n");
            sb.Append("#             noSlot      = men whose slot WorldPosition came back invalid").Append("\n");
            sb.Append("#             detached    = Agent.IsDetachedFromFormation").Append("\n");
            sb.Append("#             catchUp     = HumanAIComponent.ShouldCatchUpWithFormation").Append("\n");
            sb.Append("#             flUnreach/flDeep = mission totals of Frontline step FRAMES dropped because the step had no").Append("\n");
            sb.Append("#                           navmesh path (rock/wall) / crossed walled-off deep water; flProbes = engine").Append("\n");
            sb.Append("#                           reachability queries behind flUnreach; flPress = step pins released because the").Append("\n");
            sb.Append("#                           man pressed against something for 2 s without moving 0.15 m").Append("\n");
            sb.Append("#             flDec       = men by current Frontline decision: Attack/BackStep/FindAlly/FlankLeft/FlankRight/").Append("\n");
            sb.Append("#                           Rest/none (no decision state); closeUp = of the Attack ones, those walking up behind an").Append("\n");
            sb.Append("#                           ally in their lane instead of charging through. Only acted on under a charge (frameDis)").Append("\n");
            sb.Append("#             nnMean/nnP10/nnP50 = distance (m) from each man to his nearest formation-mate; packed = men").Append("\n");
            sb.Append("#                           with one closer than 0.7 m (bunching)").Append("\n");
            sb.Append("#             rangedArr   = mission totals of RBM's ranged Line/Loose/loose-Circle changes by reason").Append("\n");
            sb.Append("#                           (default/post/ground/fire/cavalry/heavyFire, surrounded/ring = loose circle,").Append("\n");
            sb.Append("#                           Utilities.ChooseRangedArrangement); spacingFix = loose-circle spacings found").Append("\n");
            sb.Append("#                           reset and set again").Append("\n");            sb.Append("#             retreating  = Agent.IsRetreating()   runAway = Agent.IsRunningAway").Append("\n");
            sb.Append("#             walk        = Agent.WalkMode (walking, not running)").Append("\n");
            sb.Append("#             frameDis    = !Agent.IsFormationFrameEnabled (SetFormationFrameDisabled fired)").Append("\n");
            sb.Append("#             alarmed/cautious = Agent.AIStateFlags & AlarmStateMask (a state, not free bits)").Append("\n");
            sb.Append("#             otherForm   = men whose Agent.Formation is not the formation being logged").Append("\n");
            sb.Append("#             maxSpdLimLt1= men with GetMaximumSpeedLimit() < 1").Append("\n");
            sb.Append("#             mulMean/Min = AgentDrivenProperties.MaxSpeedMultiplier").Append("\n");
            sb.Append("#             fwdMean/Min = GetMaximumForwardUnlimitedSpeed()").Append("\n");
            sb.Append("#             velMean     = MovementVelocity.Length   spdLimMean = GetCurrentSpeedLimit()").Append("\n");
            sb.Append("#").Append("\n");
            sb.Append("#   LAG    t  LAG  team  formationIndex  then up to ").Append(LagCount).Append(" stragglers, worst slot").Append("\n");
            sb.Append("#          distance first, each one pipe separated:").Append("\n");
            sb.Append("#             charStringId#agentIndex | slotDist | maxSpeedMultiplier | maximumSpeedLimit").Append("\n");
            sb.Append("#             | fwdUnlimitedSpeed | wWalkMode | vVelocity | detDetached | catchCatchUp").Append("\n");
            sb.Append("#             | fdFrameDisabled | aiStateFlags | currentActionType(0) | sameFormFlag").Append("\n");
            sb.Append("#").Append("\n");
            sb.Append("#   RUN    t  RUN  team  formationIndex  count=N  then up to ").Append(LagCount).Append(" men more than ")
                .Append(AiBehaviorLog.Fmt(RunAheadMin)).Append(" m IN FRONT of their slot").Append("\n");
            sb.Append("#          (GetCurrentGlobalPositionOfUnit, along CurrentDirection), furthest first; only written when N > 0:").Append("\n");
            sb.Append("#             charStringId#agentIndex | slot+metres ahead of slot | avg+metres ahead of the men's average").Append("\n");
            sb.Append("#             | limMaximumSpeedLimit | vVelocity | catchCatchUp | currentActionType(0)").Append("\n");
            sb.Append("#").Append("\n");
            sb.Append("#   DECISION t  DECISION  team  formationIndex  mainClass  men=N  tactic=X  beh=Y  ai=0|1  chain=...  notes=...").Append("\n");
            sb.Append("#          WHO changed the formation (DecisionTrace, observation only), at most one line per formation per 0.5 s,").Append("\n");
            sb.Append("#          everything since its last DECISION line. chain = ' | '-separated  field:writer before->after @t").Append("\n");
            sb.Append("#             field  = move (order kind changed, Move point > 5 m from the last recorded, new ChargeToTarget target;").Append("\n");
            sb.Append("#                      ' asked=K' = the order before ChargeGuard rewrote it, ' raw=Charge' = before the Charge ->").Append("\n");
            sb.Append("#                      ChargeToTarget conversion) | facing (kind, or turned > 15 deg; swept after each scope, the").Append("\n");
            sb.Append("#                      setter is JIT-inlined) | arr (ArrangementOrder) | width (FormOrder: kind, or Custom width > 10 %)").Append("\n");
            sb.Append("#                      | beh (FindBestBehavior switched the active behavior) | tactic (the team's tactic changed in").Append("\n");
            sb.Append("#                      MakeDecision, written on every formation of the team)").Append("\n");
            sb.Append("#             writer = formAI:<active behavior>[@fN:Class when ordering another formation] (FormationAI.TickOccasionally)").Append("\n");
            sb.Append("#                      | tactic:<current tactic> (TeamAIComponent.TickOccasionally) | decide:<tactic> (MakeDecision,").Append("\n");
            sb.Append("#                      RBM re-picks included) | chargeGuard (its Formation.Tick re-apply; '<writer>>chargeGuard' = the").Append("\n");
            sb.Append("#                      writer's charge turned into ChargeGuard's advance Move or re-aimed) | react (ReactionMonitor check)").Append("\n");
            sb.Append("#                      | player (player-controlled formation, no scope) | other (no scope); a trailing ~ = arr/width").Append("\n");
            sb.Append("#                      seen by the after-scope sweep, not by its setter hook").Append("\n");
            sb.Append("#             notes  = the formation's BEHAVIOR / REACT events in between (Behavior what why @t; older than 30 s dropped)").Append("\n");
            sb.Append("#").Append("\n");
            sb.Append("#   FLIP   t  FLIP  team  formationIndex  mainClass  field=move|order|arr|beh  count=N/30s  writers=W xN,...  seq=v@t>...").Append("\n");
            sb.Append("#          a formation that keeps changing its mind: Move point jumping > 20 m back against its previous jump more than").Append("\n");
            sb.Append("#          3 times (writers 'previous/this'), or order kind / arrangement / behavior changing more than 2 times, in 30 s;").Append("\n");
            sb.Append("#          once per field per 30 s. The writers named are the rules that fight.").Append("\n");
            sb.Append("#").Append("\n");
            sb.Append("# Snapshots every ").Append(AiBehaviorLog.Fmt(SnapshotInterval)).Append("s; AGENTS/LAG every ")
                .Append(AiBehaviorLog.Fmt(AgentsInterval)).Append("s.").Append("\n");
            sb.Append("#").Append("\n");
            return sb.ToString().Replace("\n", Environment.NewLine);
        }
    }
}
