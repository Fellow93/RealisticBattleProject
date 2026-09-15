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
    /// Writes down, second by second, what every team's AI has decided and what every formation is actually doing.
    ///
    /// The reason it exists: the enemy team's infantry and the player's delegated team run the same code, and yet
    /// they do not behave the same. Everything that could explain the difference is invisible during a battle --
    /// which tactic the team AI picked, whether that tactic thinks the battle has been joined, which behavior won
    /// the weight contest inside each formation, what movement order came out of it, how far the formation is from
    /// the position it was ordered to. This log makes all of it a column.
    ///
    /// Four kinds of line are written. A periodic snapshot every second, so the whole battle can be read as a
    /// timeline; an immediate CHANGE line the moment a tactic, a behavior or a movement order flips, so the
    /// exact instant of a decision is not lost between two snapshots; and, every five seconds, an AGENTS/LAG
    /// pair for each infantry formation.
    ///
    /// The AGENTS pair was added after the first log answered the formation-level question and replaced it with
    /// a harder one: both teams ran the same tactic, the same behavior, the same cached movement speed and the
    /// same distance to their order, and yet one line stayed 15m ragged while the other closed to 3m. Nothing at
    /// the formation level can explain that, so AGENTS measures every man against the slot his own formation
    /// assigned him -- and LAG names the eight furthest behind, with the speed, mode and state flags that would
    /// explain why.
    ///
    /// Everything reflective is wrapped: a field that moved in a game update makes a column say "?" and never
    /// takes the mission down with it.
    /// </summary>
    public class AiBehaviorLogic : MissionLogic
    {
        /// <summary>How often the full snapshot is written.</summary>
        private const float SnapshotInterval = 1f;

        /// <summary>How often the per-agent AGENTS/LAG pair is written. Rarer: it walks every man in the line.</summary>
        private const float AgentsInterval = 5f;

        /// <summary>How many stragglers the LAG line names.</summary>
        private const int LagCount = 8;

        private bool _logging;

        private float _nextSnapshot;

        private float _nextAgents;

        /// <summary>Last-seen tactic type per team, so a change can be caught the frame it happens.</summary>
        private readonly Dictionary<Team, string> _lastTactic = new Dictionary<Team, string>();

        /// <summary>Last-seen behavior type and movement order per formation, for the same reason.</summary>
        private readonly Dictionary<Formation, string> _lastBehavior = new Dictionary<Formation, string>();

        private readonly Dictionary<Formation, string> _lastOrder = new Dictionary<Formation, string>();

        /// <summary>The reflection misses already reported. A broken field says so once, not sixty times a second.</summary>
        private readonly HashSet<string> _reportedFailures = new HashSet<string>();

        // ---- reflection handles, resolved once ----

        private static readonly FieldInfo _fCurrentTactic =
            AccessTools.Field(typeof(TeamAIComponent), "_currentTactic");

        private static readonly FieldInfo _fReapplyNeeded =
            AccessTools.Field(typeof(TacticComponent), "IsTacticReapplyNeeded");

        /// <summary>_hasBattleBeenJoined is declared per concrete tactic, so it is looked up per type and cached.</summary>
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
            _nextSnapshot = 0f;
            _nextAgents = 0f;
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
            AiBehaviorLog.EndMission("# mission ended at t=" + AiBehaviorLog.Fmt(
                (Mission.Current != null) ? Mission.Current.CurrentTime : 0f));
            _logging = false;
        }

        // ------------------------------------------------------------------

        private void TickTeam(Team team, float now, bool snapshot, bool agentsSnapshot)
        {
            TacticComponent tactic = null;
            if (_fCurrentTactic != null)
            {
                tactic = _fCurrentTactic.GetValue(team.TeamAI) as TacticComponent;
            }
            string tacticName = (tactic != null) ? tactic.GetType().Name : "-";

            string previous;
            bool tacticChanged = !_lastTactic.TryGetValue(team, out previous) || previous != tacticName;
            if (tacticChanged)
            {
                _lastTactic[team] = tacticName;
                // The very first sighting of a team is not a change worth a CHANGE line unless it is mid-battle.
                if (previous != null)
                {
                    AiBehaviorLog.Write("t=" + AiBehaviorLog.Fmt(now) + "\tCHANGE\tTACTIC\t" + TeamName(team)
                        + "\t" + previous + " -> " + tacticName);
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

        // ---------------- per-agent snapshot ------------------------------

        /// <summary>
        /// The formation-level columns say the two teams are given the same orders; the per-agent ones say whether
        /// the men obey them. Everything here is read off the agent itself, on the main thread, once every five
        /// seconds -- the slot lookup alone is a navmesh query per man, so it is not something to do per frame.
        /// </summary>
        private struct AgentSample
        {
            public Agent Agent;
            public float SlotDist;      // negative = no slot could be read
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
        }

        private AgentSample Sample(Formation formation, Agent agent)
        {
            AgentSample s = new AgentSample();
            s.Agent = agent;

            // Slot: the world position the formation currently wants this man to stand on.
            s.SlotDist = -1f;
            try
            {
                WorldPosition slot = formation.GetOrderPositionOfUnit(agent);
                if (slot.IsValid)
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
                // SetFormationFrameDisabled() clears IsFormationFrameEnabled; there is no separate getter.
                s.FrameDisabled = !agent.IsFormationFrameEnabled;
            }
            catch (Exception e)
            {
                ReportOnce("isFormationFrameEnabled", e);
            }

            try
            {
                Agent.AIStateFlag flags = agent.AIStateFlags;
                // The low two bits are an alarm *state*, not independent bits: Alarmed is 3, which is
                // Cautious|PatrollingCautious. Mask first, then compare, or every alarmed man reads as cautious.
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
            // Global counters from the worker-thread postfix: how many times it ran / asserted catch-up
            // since mission start. If both stay 0 the patch is not live (stale DLL or the MissionLibrary early-out).
            sb.Append('\t').Append("pfxCalls=").Append(OverrideParallelFormationMovement.CallCount);
            sb.Append('\t').Append("pfxSet=").Append(OverrideParallelFormationMovement.SetCount);
            sb.Append('\t').Append("devCaps=").Append(FormationCatchUpGate.CapCount);
            sb.Append('\t').Append("retreating=").Append(retreating);
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

        /// <summary>Nearest-rank on an already sorted list. Good enough for a diagnostic, and allocation free.</summary>
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

        // ------------------------------------------------------------------

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
            return sb.ToString();
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
            sb.Append('\t').Append("weights=").Append(BehaviorWeights(formation));
            return sb.ToString();
        }

        /// <summary>
        /// The three behaviors currently bidding highest inside the formation. FormationAI exposes its list through
        /// BehaviorCount / GetBehaviorAtIndex, so nothing here has to reach into a private field. GetAIWeight is the
        /// same call the AI itself makes twice a second; asking once more per second changes nothing it decides.
        /// </summary>
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

        // ------------------------------------------------------------------

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

        /// <summary>A miss is written down once and then never again: a log that repeats itself is unreadable.</summary>
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
            // Is the worker-thread formation-movement postfix actually installed? (pfxCalls=0 on 2026-09-09.)
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
            sb.Append("#          teamAIType  tacticType  joined  reapply  aiForms  allowAiTicking").Append("\n");
            sb.Append("#             joined  = the tactic's own _hasBattleBeenJoined (n/a if that tactic has none)").Append("\n");
            sb.Append("#             reapply = TacticComponent.IsTacticReapplyNeeded").Append("\n");
            sb.Append("#             aiForms = Team.GetAIControlledFormationCount()").Append("\n");
            sb.Append("#").Append("\n");
            sb.Append("#   FORM   t  FORM  team  formationIndex  primaryClass  units  aiControlled  playerIn").Append("\n");
            sb.Append("#          playerLed  activeBehavior  side  movementOrder  state  arr  form").Append("\n");
            sb.Append("#          w  d  spd  spdMax  dev  idealDisp  avg  order  distToOrder  distToEnemy  weights").Append("\n");
            sb.Append("#             state       = Formation.GetMovementState()").Append("\n");
            sb.Append("#             arr/form    = ArrangementOrder / FormOrder enum").Append("\n");
            sb.Append("#             spd/spdMax  = CachedMovementSpeed / QuerySystem.MovementSpeedMaximum").Append("\n");
            sb.Append("#             dev         = DeviationOfPositionsExcludeFarAgents (how ragged the line is)").Append("\n");
            sb.Append("#             idealDisp   = QuerySystem.IdealAverageDisplacement").Append("\n");
            sb.Append("#             avg/order   = CachedAveragePosition / OrderPosition, as x,y").Append("\n");
            sb.Append("#             distToEnemy = sqrt(CachedClosestEnemyFormationDistanceSquared)").Append("\n");
            sb.Append("#             weights     = top 3 behaviors as Name:aiWeight*weightFactor/cCoherence").Append("\n");
            sb.Append("#").Append("\n");
            sb.Append("#   CHANGE t  CHANGE  TACTIC|BEHAVIOR|ORDER  team  [formation]  old -> new").Append("\n");
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
            sb.Append("#             retreating  = Agent.IsRetreating()   runAway = Agent.IsRunningAway").Append("\n");
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
            sb.Append("# Snapshots every ").Append(AiBehaviorLog.Fmt(SnapshotInterval)).Append("s; AGENTS/LAG every ")
                .Append(AiBehaviorLog.Fmt(AgentsInterval)).Append("s.").Append("\n");
            sb.Append("#").Append("\n");
            return sb.ToString().Replace("\n", Environment.NewLine);
        }
    }
}
