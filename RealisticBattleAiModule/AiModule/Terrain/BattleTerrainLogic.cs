using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using RBMConfig.Logging;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// Computes and publishes BattleTerrain.Current once per land field battle and writes the optional terrain log
    /// (logs/terrain, battleTerrainLoggingEnabled; ASCII only; line kinds in docs/terrain-log-format.md). Added under
    /// rbmAiEnabled, not gated on the log toggle; field battles only. The main thread gathers engine data on the first
    /// tick after deployment and starts the background Compute; later ticks poll it and publish on the main thread. A
    /// result from an older generation is dropped. Cleared at mission end and by MissionStartReset.
    ///
    /// At the publish (log on or off) it builds and publishes, in order: terrain tactic factors (and drops TacticDwell's
    /// hold once, so the terrain-blind first pick is re-made), flank plan, archer spots, cavalry ground,
    /// choke hold, battle position (+ reserve plug), hedgehog read, attacker approach; the Terrain* classes read them on the AI thread
    /// (WorldPositions are built here on the main thread). Each tick it keeps the plans current (flank plan, cavalry /
    /// spots re-plans, battle-position re-pick) and drains the AI-thread log queues (FLANKUSE, BEHAVIOR, REACT,
    /// REPICK, FORMSHAPE). None of it uses ground within EdgeMargin of the map edge.
    ///
    /// With the log on it also writes PREDICT per team at the publish, a TACTIC line per real tactic change and
    /// TACTICSUMMARY at mission end; these feed nothing. State is instance fields, cleared at mission start and end.
    /// </summary>
    public class BattleTerrainLogic : MissionLogic
    {
        /// <summary>Bumped (main thread) at every mission start and end; a background result from another generation is dropped.</summary>
        private static int _generation;

        private BufferedLogWriter _log;

        /// <summary>A land field battle: the grid was attempted and the read is due.</summary>
        private bool _active;

        /// <summary>The gather runs once, on the first tick after deployment.</summary>
        private bool _terrainRead;

        /// <summary>The whole-map grid, built in AfterStart; handed to the gather's bundle (and on to the snapshot).</summary>
        private BattleTerrainRead.Grid _terrainGrid;

        /// <summary>The background part while it runs; null before the gather and once polled.</summary>
        private Task<BattleTerrain> _pending;

        private int _pendingGeneration;

        private long _pendingStartTicks;

        private int _pendingFrames;

        /// <summary>"deployment" or "noDeployment": what triggered the gather.</summary>
        private string _pendingSource;

        /// <summary>A failure is written down once per key, never per frame.</summary>
        private readonly HashSet<string> _reportedFailures = new HashSet<string>();

        /// <summary>Tactic log watch, one entry per team with team AI; filled at the publish when the log is on, summarised and cleared at mission end. Main thread only.</summary>
        private readonly List<TacticWatch> _tacticWatches = new List<TacticWatch>();

        private readonly List<string> _flankLines = new List<string>();

        private readonly List<string> _replanLines = new List<string>();

        /// <summary>One team's tactic prediction: its side's (immutable) terrain factors and what was last seen/predicted.</summary>
        private sealed class TacticWatch
        {
            public Team Team;
            public TerrainTacticFactors.SideFactors Factors;
            public TacticComponent Real;
            /// <summary>The predicted pick at the last evaluation (publish or last change).</summary>
            public TacticComponent Pick;
            /// <summary>Real == Pick, as of the last evaluation or change.</summary>
            public bool Agree;
            /// <summary>Real tactic changes after the first sighting.</summary>
            public int Changes;
            /// <summary>Mission time up to which WatchedSeconds/AgreeSeconds are summed.</summary>
            public float LastTime;
            public float WatchedSeconds;
            public float AgreeSeconds;
        }

        public override void AfterStart()
        {
            base.AfterStart();
            _generation++;
            _active = false;
            _terrainRead = false;
            _terrainGrid = null;
            _pending = null;
            _reportedFailures.Clear();
            _tacticWatches.Clear();
            _compositionLogged.Clear();
            BattleTerrain.Clear();
            TerrainTacticFactors.Clear();
            TerrainFlankPlan.Clear();
            TerrainSpots.Clear();
            TerrainCavalry.Clear();
            TerrainChokeHold.Clear();
            TerrainBattlePosition.Clear();
            TerrainHedgehog.Clear();
            TerrainApproach.Clear();
            TerrainShape.Clear();
            ReactionMonitor.Clear();
            Mission mission = Mission;
            if (mission == null || mission.Scene == null || !mission.IsFieldBattle)
            {
                return;
            }
            _active = true;

            if (RBMConfig.RBMConfig.battleTerrainLoggingEnabled)
            {
                OpenLog();
                try
                {
                    Log(Header(mission));
                }
                catch (Exception e)
                {
                    ReportOnce("header", e);
                }
            }

            try
            {
                _terrainGrid = BattleTerrainRead.Build(mission);
            }
            catch (Exception e)
            {
                ReportOnce("terrainGrid", e);
            }
            Flush();
        }

        public override void OnMissionTick(float dt)
        {
            if (!_active)
            {
                return;
            }
            Mission mission = Mission;
            if (mission == null)
            {
                return;
            }
            if (!ArmyScale.Latched)
            {
                // army scale: latched once the spawn logic has its totals, forced at the publish
                try
                {
                    ArmyScale.TryLatch(mission, false);
                }
                catch (Exception e)
                {
                    ReportOnce("armyScale", e);
                }
            }
            if (_pending != null)
            {
                PollPending(mission);
                return;
            }
            if (_terrainRead)
            {
                LogRepicks();
                TickFlankPlan(mission);
                TickArmyPlans(mission);
                RepickBattlePosition(mission);
                LogTerrainEvents();
                LogReactions();
                LogComposition(mission);
                if (_tacticWatches.Count > 0)
                {
                    WatchTactics(mission);
                }
                return;
            }

            // A battle without a deployment phase never sets IsDeploymentFinished: read once the AI has ticked a few seconds.
            float now = mission.CurrentTime;
            if (!(mission.IsDeploymentFinished || (mission.AllowAiTicking && now > 3f)))
            {
                return;
            }

            _terrainRead = true;
            _pendingSource = mission.IsDeploymentFinished ? "deployment" : "noDeployment";
            try
            {
                // main thread: engine values copied into an immutable bundle
                string skipped;
                BattleTerrainRead.Inputs input = BattleTerrainRead.Gather(mission, _terrainGrid, out skipped);
                if (input == null)
                {
                    Log("t=" + Fmt(now) + "\tTERRAIN\tsrc=" + _pendingSource + "\tskipped (" + (skipped ?? "?") + ")");
                }
                else
                {
                    // background: grid work on the bundle only
                    _pendingGeneration = _generation;
                    _pendingStartTicks = input.StartTicks;
                    _pendingFrames = 0;
                    _pending = Task.Run(() => BattleTerrainRead.Compute(input));
                    Log("# gathered at t=" + Fmt(now) + ": mainMs=" + input.MainMs + " (rayMs=" + input.RayMs
                        + "); the background part is running, TERRAIN lines follow when it is published");
                }
            }
            catch (Exception e)
            {
                ReportOnce("terrain", e);
            }
            _terrainGrid = null;
            Flush();
        }

        /// <summary>Main thread: publishes and logs the background result once it has finished.</summary>
        private void PollPending(Mission mission)
        {
            _pendingFrames++;
            Task<BattleTerrain> task = _pending;
            if (!task.IsCompleted)
            {
                return;
            }
            _pending = null;
            if (_pendingGeneration != _generation)
            {
                // started under another mission: never publish it into this one
                return;
            }
            float now = mission.CurrentTime;
            if (task.IsFaulted || task.IsCanceled)
            {
                Exception e = (task.Exception != null) ? task.Exception.GetBaseException() : new OperationCanceledException("background terrain task canceled");
                ReportOnce("terrainBackground", e);
                Flush();
                return;
            }
            try
            {
                BattleTerrain terrain = task.Result;
                if (terrain == null)
                {
                    return;
                }
                terrain.PublishedAt = now;
                terrain.WallMs = (DateTime.UtcNow.Ticks - _pendingStartTicks) / TimeSpan.TicksPerMillisecond;
                // complete before publishing; nothing writes to it afterwards
                BattleTerrain.Publish(terrain);
                try
                {
                    // before the plans below: their ForSide / PlanFor / HoldFor read null for a small side (ArmyScale)
                    ArmyScale.TryLatch(mission, true);
                }
                catch (Exception e)
                {
                    ReportOnce("armyScale", e);
                }
                TerrainTacticFactors.SideFactors attacker = null;
                TerrainTacticFactors.SideFactors defender = null;
                TerrainFlankPlan.SidePlan attackerFlanks = null;
                TerrainFlankPlan.SidePlan defenderFlanks = null;
                TerrainSpots.SidePlan attackerSpots = null;
                TerrainSpots.SidePlan defenderSpots = null;
                TerrainCavalry.SidePlan attackerCavalry = null;
                TerrainCavalry.SidePlan defenderCavalry = null;
                TerrainChokeHold.Plan defenderChoke = null;
                string chokeWhy = null;
                TerrainBattlePosition.Plan defenderPosition = null;
                string positionWhy = null;
                TerrainHedgehog.SidePlan attackerHedgehog = null;
                TerrainHedgehog.SidePlan defenderHedgehog = null;
                TerrainApproach.SidePlan attackerApproach = null;
                int repicks = -1;
                try
                {
                    attacker = TerrainTacticFactors.Compute(terrain, BattleSideEnum.Attacker);
                    defender = TerrainTacticFactors.Compute(terrain, BattleSideEnum.Defender);
                    // complete and immutable before publishing; the AI thread reads them from here on
                    TerrainTacticFactors.Publish(attacker, defender);
                    // armies are on the field now: compositions restart from the real numbers
                    ArmyComposition.RequestReseed();
                    try
                    {
                        // flank plan; read only with terrainTacticsEnabled
                        TerrainFlankPlan.Compute(terrain, out attackerFlanks, out defenderFlanks);
                        TerrainFlankPlan.Publish(attackerFlanks, defenderFlanks);
                    }
                    catch (Exception e)
                    {
                        ReportOnce("flankPlan", e);
                    }
                    try
                    {
                        // archer spots: WorldPositions built HERE on the main thread (behaviors only copy them on the AI thread)
                        TerrainSpots.Build(mission, terrain, out attackerSpots, out defenderSpots);
                        TerrainSpots.Publish(attackerSpots, defenderSpots);
                    }
                    catch (Exception e)
                    {
                        ReportOnce("archerSpots", e);
                    }
                    try
                    {
                        // cavalry ground: lane and reform WorldPositions built HERE on the main thread
                        TerrainCavalry.Build(mission, terrain, out attackerCavalry, out defenderCavalry);
                        TerrainCavalry.Publish(attackerCavalry, defenderCavalry, terrain.Cavalry);
                    }
                    catch (Exception e)
                    {
                        ReportOnce("cavalryGround", e);
                    }
                    try
                    {
                        // choke hold: WorldPositions built HERE; published before the terrain re-pick below, which weighs it
                        defenderChoke = TerrainChokeHold.Build(mission, terrain, out chokeWhy);
                    }
                    catch (Exception e)
                    {
                        ReportOnce("chokeHold", e);
                    }
                    TerrainChokeHold.Publish(defenderChoke);
                    try
                    {
                        // battle position: WorldPositions built HERE; published before the terrain re-pick
                        defenderPosition = TerrainBattlePosition.Build(mission, terrain, terrain.BattlePosition.Result, 1, now, out positionWhy);
                    }
                    catch (Exception e)
                    {
                        ReportOnce("battlePosition", e);
                    }
                    TerrainBattlePosition.Publish(defenderPosition);
                    try
                    {
                        // hedgehog read: agents are read HERE on the main thread; published before the terrain re-pick
                        attackerHedgehog = TerrainHedgehog.Build(mission, terrain, BattleSideEnum.Attacker);
                        defenderHedgehog = TerrainHedgehog.Build(mission, terrain, BattleSideEnum.Defender);
                    }
                    catch (Exception e)
                    {
                        ReportOnce("hedgehog", e);
                    }
                    TerrainHedgehog.Publish(attackerHedgehog, defenderHedgehog);
                    try
                    {
                        // attacker approach: waypoint WorldPositions built HERE
                        attackerApproach = TerrainApproach.Build(mission, terrain, terrain.Approach.Result, 1, "publish");
                        TerrainApproach.Publish(attackerApproach);
                    }
                    catch (Exception e)
                    {
                        ReportOnce("approach", e);
                    }
                    if (RBMConfig.RBMConfig.terrainTacticsEnabled && (attacker != null || defender != null))
                    {
                        // native's x1.5 current-tactic bonus would keep the terrain-blind first pick: re-pick once without it (runs whatever TacticDwell holds)
                        repicks = TacticSelection.RequestTerrainRepick(mission);
                    }
                }
                catch (Exception e)
                {
                    ReportOnce("tacticFactors", e);
                }
                if (_log != null)
                {
                    Log("t=" + Fmt(now) + "\tTERRAIN\tsrc=" + _pendingSource + "\t" + BattleTerrainLogFormat.Base(terrain)
                        + " frames=" + _pendingFrames.ToString(CultureInfo.InvariantCulture));
                    foreach (string line in BattleTerrainLogFormat.Groups(terrain))
                    {
                        Log("t=" + Fmt(now) + "\t" + line);
                    }
                    foreach (string line in BattleTerrainLogFormat.Features(terrain))
                    {
                        Log("t=" + Fmt(now) + "\t" + line);
                    }
                    Log("# terrain tactic factors published at t=" + Fmt(now) + ": applied=" + (RBMConfig.RBMConfig.terrainTacticsEnabled ? 1 : 0)
                        + ((repicks >= 0)
                            ? "; terrain re-pick (no x1.5 current-tactic bonus, margin x"
                              + Fmt(TacticSelection.TerrainRepickMargin) + ") due for " + repicks.ToString(CultureInfo.InvariantCulture)
                              + " team(s) at their next decision (<= 5 s)"
                            : ""));
                    foreach (BattleSideEnum side in new[] { BattleSideEnum.Attacker, BattleSideEnum.Defender })
                    {
                        ArmyScale.SideScale scale = ArmyScale.For(side);
                        Log("t=" + Fmt(now) + "\tSCALE\t" + side + "\t" + ((scale != null) ? scale.Describe() : "unread"));
                    }
                    LogFlankPlan(attackerFlanks, now);
                    LogFlankPlan(defenderFlanks, now);
                    try
                    {
                        Log("t=" + Fmt(now) + "\tSPOTS\t" + BattleSideEnum.Attacker + "\t" + TerrainSpots.Describe(terrain.Spots, terrain.Spots.Attacker, attackerSpots));
                        Log("t=" + Fmt(now) + "\tSPOTS\t" + BattleSideEnum.Defender + "\t" + TerrainSpots.Describe(terrain.Spots, terrain.Spots.Defender, defenderSpots));
                    }
                    catch (Exception e)
                    {
                        ReportOnce("spotsLog", e);
                    }
                    try
                    {
                        Log("t=" + Fmt(now) + "\tCAVALRY\t" + BattleSideEnum.Attacker + "\t" + TerrainCavalry.Describe(terrain.Cavalry, terrain.Cavalry.Attacker, attackerCavalry));
                        Log("t=" + Fmt(now) + "\tCAVALRY\t" + BattleSideEnum.Defender + "\t" + TerrainCavalry.Describe(terrain.Cavalry, terrain.Cavalry.Defender, defenderCavalry));
                    }
                    catch (Exception e)
                    {
                        ReportOnce("cavalryLog", e);
                    }
                    try
                    {
                        Log("t=" + Fmt(now) + "\tCHOKEHOLD\t" + BattleSideEnum.Defender + "\t" + TerrainChokeHold.Describe(terrain.ChokeHold, defenderChoke, chokeWhy));
                    }
                    catch (Exception e)
                    {
                        ReportOnce("chokeHoldLog", e);
                    }
                    try
                    {
                        Log("t=" + Fmt(now) + "\tBATTLEPOS\t" + BattleSideEnum.Defender + "\t"
                            + TerrainBattlePosition.Describe(terrain.BattlePosition, terrain.BattlePosition.Result, defenderPosition, positionWhy));
                    }
                    catch (Exception e)
                    {
                        ReportOnce("battlePositionLog", e);
                    }
                    try
                    {
                        Log("t=" + Fmt(now) + "\tRESERVE\t" + BattleSideEnum.Defender + "\t"
                            + TerrainBattlePosition.DescribePlug(terrain.BattlePosition.Result, defenderPosition));
                    }
                    catch (Exception e)
                    {
                        ReportOnce("reservePlugLog", e);
                    }
                    try
                    {
                        Log("t=" + Fmt(now) + "\tHEDGEHOG\t" + BattleSideEnum.Attacker + "\t" + TerrainHedgehog.Describe(attackerHedgehog));
                        Log("t=" + Fmt(now) + "\tHEDGEHOG\t" + BattleSideEnum.Defender + "\t" + TerrainHedgehog.Describe(defenderHedgehog));
                    }
                    catch (Exception e)
                    {
                        ReportOnce("hedgehogLog", e);
                    }
                    try
                    {
                        Log("t=" + Fmt(now) + "\tAPPROACH\t" + BattleSideEnum.Attacker + "\t" + TerrainApproach.Describe(terrain.Approach.Result, attackerApproach)
                            + ((terrain.Approach.Why != null) ? "\tstep=" + terrain.Approach.Why : "")
                            + ((terrain.Approach.Error != null) ? "\terror=" + terrain.Approach.Error : ""));
                    }
                    catch (Exception e)
                    {
                        ReportOnce("approachLog", e);
                    }
                    if (!float.IsNaN(terrain.Outlines.Ms))
                    {
                        // traced in developer mode only
                        Log("t=" + Fmt(now) + "\t" + OutlinesLine(terrain.Outlines));
                    }
                    try
                    {
                        StartTacticPrediction(mission, attacker, defender, terrain, now);
                    }
                    catch (Exception e)
                    {
                        ReportOnce("tacticPredict", e);
                    }
                }
            }
            catch (Exception e)
            {
                ReportOnce("terrainPublish", e);
            }
            Flush();
        }

        // terrain tactic log (feeds nothing; main thread)

        /// <summary>At the publish, log on: per team one PREDICT line and the first TACTIC line; the team is watched from the next tick.</summary>
        private void StartTacticPrediction(Mission mission, TerrainTacticFactors.SideFactors attacker, TerrainTacticFactors.SideFactors defender,
            BattleTerrain terrain, float now)
        {
            _tacticWatches.Clear();
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.HasTeamAi)
                {
                    continue;
                }
                TerrainTacticFactors.SideFactors factors = (team.Side == BattleSideEnum.Attacker) ? attacker
                    : (team.Side == BattleSideEnum.Defender) ? defender : null;
                if (factors == null)
                {
                    continue;
                }
                try
                {
                    TacticWatch watch = new TacticWatch();
                    watch.Team = team;
                    watch.Factors = factors;
                    watch.LastTime = now;
                    TacticComponent real = TacticWeightProbe.CurrentTactic(team);
                    bool applied = TerrainApplied(team);
                    List<OptionScore> scores = Score(TacticWeightProbe.Evaluate(team), factors, applied);
                    TacticComponent pick = Pick(scores, true);
                    TacticComponent basePick = Pick(scores, false);
                    watch.Real = real;
                    watch.Pick = pick;
                    watch.Agree = pick != null && pick == real;
                    Log(PredictLine(team, factors, scores, applied, pick, basePick, real, terrain, now));
                    Log(TacticLine(watch, null, real, scores, applied, pick, basePick, now));
                    _tacticWatches.Add(watch);
                }
                catch (Exception e)
                {
                    ReportOnce("tacticPredict", e);
                }
            }
        }

        /// <summary>Every tick: one reflection read of each watched team's tactic; on a change the weights are re-evaluated and a TACTIC line written.</summary>
        private void WatchTactics(Mission mission)
        {
            if (_log == null)
            {
                _tacticWatches.Clear();
                return;
            }
            float now = mission.CurrentTime;
            bool wrote = false;
            foreach (TacticWatch watch in _tacticWatches)
            {
                Accumulate(watch, now);
                TacticComponent real;
                try
                {
                    real = TacticWeightProbe.CurrentTactic(watch.Team);
                }
                catch (Exception e)
                {
                    ReportOnce("tacticWatch", e);
                    continue;
                }
                if (real == watch.Real)
                {
                    continue;
                }
                TacticComponent old = watch.Real;
                watch.Real = real;
                watch.Changes++;
                watch.Agree = watch.Pick != null && watch.Pick == real;
                try
                {
                    bool applied = TerrainApplied(watch.Team);
                    List<OptionScore> scores = Score(TacticWeightProbe.Evaluate(watch.Team), watch.Factors, applied);
                    TacticComponent pick = Pick(scores, true);
                    TacticComponent basePick = Pick(scores, false);
                    watch.Pick = pick;
                    watch.Agree = pick != null && pick == real;
                    Log(TacticLine(watch, old, real, scores, applied, pick, basePick, now));
                    wrote = true;
                }
                catch (Exception e)
                {
                    ReportOnce("tacticWatch", e);
                }
            }
            if (wrote)
            {
                Flush();
            }
        }

        /// <summary>Sums the mission seconds since the last call into the team's watched / agreeing time.</summary>
        private static void Accumulate(TacticWatch watch, float now)
        {
            float elapsed = now - watch.LastTime;
            if (elapsed > 0f)
            {
                watch.WatchedSeconds += elapsed;
                if (watch.Agree)
                {
                    watch.AgreeSeconds += elapsed;
                }
            }
            watch.LastTime = now;
        }

        /// <summary>At mission end, log on: one TACTICSUMMARY line per watched team; then the watch is cleared.</summary>
        private void WriteTacticSummaries(float now)
        {
            foreach (TacticWatch watch in _tacticWatches)
            {
                try
                {
                    Accumulate(watch, now);
                    float share = (watch.WatchedSeconds > 0f) ? watch.AgreeSeconds / watch.WatchedSeconds : float.NaN;
                    Log("t=" + Fmt(now) + "\tTACTICSUMMARY\t" + TeamName(watch.Team)
                        + "\tchanges=" + watch.Changes.ToString(CultureInfo.InvariantCulture)
                        + "\tagreeShare=" + Fmt(share)
                        + "\tagreeS=" + Fmt(watch.AgreeSeconds)
                        + "\twatchedS=" + Fmt(watch.WatchedSeconds)
                        + "\tlastPick=" + TacticWeightProbe.Name(watch.Pick)
                        + "\treal=" + TacticWeightProbe.Name(watch.Real));
                }
                catch (Exception e)
                {
                    ReportOnce("tacticSummary", e);
                }
            }
            _tacticWatches.Clear();
        }

        private static string PredictLine(Team team, TerrainTacticFactors.SideFactors factors, List<OptionScore> scores, bool applied,
            TacticComponent pick, TacticComponent basePick, TacticComponent real, BattleTerrain terrain, float now)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("t=").Append(Fmt(now)).Append("\tPREDICT");
            sb.Append('\t').Append(TeamName(team));
            sb.Append('\t').Append(team.Side);
            sb.Append('\t').Append("terrainAt=").Append(Fmt(terrain.PublishedAt));
            sb.Append('\t').Append("applied=").Append(applied ? 1 : 0);
            ArmyScale.SideScale scale = ArmyScale.For(team.Side);
            sb.Append('\t').Append("scale=").Append((scale != null) ? scale.Reason : "unread");
            sb.Append('\t').Append("in=").Append(factors.Inputs);
            sb.Append('\t').Append("options=").Append(DescribeOptions(scores, applied, true));
            sb.Append('\t').Append("pick=").Append(TacticWeightProbe.Name(pick));
            sb.Append('\t').Append("basePick=").Append(TacticWeightProbe.Name(basePick));
            sb.Append('\t').Append("real=").Append(TacticWeightProbe.Name(real));
            sb.Append('\t').Append("agree=").Append((pick != null && pick == real) ? 1 : 0);
            return sb.ToString();
        }

        private static string TacticLine(TacticWatch watch, TacticComponent old, TacticComponent real, List<OptionScore> scores, bool applied,
            TacticComponent pick, TacticComponent basePick, float now)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("t=").Append(Fmt(now)).Append("\tTACTIC");
            sb.Append('\t').Append(TeamName(watch.Team));
            sb.Append('\t').Append(TacticWeightProbe.Name(old)).Append(" -> ").Append(TacticWeightProbe.Name(real));
            sb.Append('\t').Append("pick=").Append(TacticWeightProbe.Name(pick));
            sb.Append('\t').Append("basePick=").Append(TacticWeightProbe.Name(basePick));
            sb.Append('\t').Append("agree=").Append((pick != null && pick == real) ? 1 : 0);
            sb.Append('\t').Append("applied=").Append(applied ? 1 : 0);
            sb.Append('\t').Append("options=").Append(DescribeOptions(scores, applied, false));
            return sb.ToString();
        }

        /// <summary>One option's weight as evaluated, its terrain factor and its score with and without that factor.</summary>
        private struct OptionScore
        {
            public TacticComponent Tactic;
            /// <summary>GetTacticWeight through RBM's patches, as evaluated (NaN when it threw).</summary>
            public float Weight;
            /// <summary>Applied: the factor TacticSelection multiplied in (TerrainFactorFor). Not applied: the side's computed total.</summary>
            public float T;
            /// <summary>The side's computed factor (raw, terms), or null for a type without one.</summary>
            public TerrainTacticFactors.Factor Factor;
            /// <summary>Score with the terrain factor: the weight itself when applied, weight x T otherwise.</summary>
            public float With;
            /// <summary>Score without it: weight / T when applied, the weight itself otherwise.</summary>
            public float Without;
        }

        /// <summary>True when the team's weights carry the terrain factor: toggle on and its side's factors published.</summary>
        private static bool TerrainApplied(Team team)
        {
            return TerrainPlans.Active(team) && TerrainTacticFactors.ForSide(team.Side) != null;
        }

        /// <summary>Scores each option with and without terrain (applied: divide the factor out; not applied: multiply it in). Never multiplies terrain twice.</summary>
        private static List<OptionScore> Score(List<KeyValuePair<TacticComponent, float>> weights, TerrainTacticFactors.SideFactors factors, bool applied)
        {
            List<OptionScore> scores = new List<OptionScore>();
            if (weights == null)
            {
                return scores;
            }
            foreach (KeyValuePair<TacticComponent, float> entry in weights)
            {
                OptionScore s = new OptionScore();
                s.Tactic = entry.Key;
                s.Weight = entry.Value;
                s.Factor = (factors != null) ? factors.Get(entry.Key.GetType()) : null;
                float w = TacticWeightProbe.SafeWeight(entry.Value);
                if (applied)
                {
                    s.T = TacticSelection.TerrainFactorFor(entry.Key);
                    s.With = w;
                    s.Without = (s.T > 0f) ? w / s.T : w;
                }
                else
                {
                    s.T = (s.Factor != null) ? s.Factor.Total : 1f;
                    s.With = w * s.T;
                    s.Without = w;
                }
                scores.Add(s);
            }
            return scores;
        }

        /// <summary>Per option: applied Name:w=W/t=F(rawR)[terms]/noTerrain=N, not applied Name:base=W/t=.../pred=P (compact: W/F/N or W/F/P). "-" with no option.</summary>
        private static string DescribeOptions(List<OptionScore> scores, bool applied, bool verbose)
        {
            if (scores == null || scores.Count == 0)
            {
                return "-";
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < scores.Count; i++)
            {
                OptionScore s = scores[i];
                float other = applied ? s.Without : s.With;
                if (i > 0)
                {
                    sb.Append(' ');
                }
                sb.Append(s.Tactic.GetType().Name).Append(':');
                if (verbose)
                {
                    sb.Append(applied ? "w=" : "base=").Append(Fmt(s.Weight))
                      .Append("/t=").Append(Fmt(s.T))
                      .Append("(raw").Append((s.Factor != null) ? Fmt(s.Factor.Raw) : "1").Append(')')
                      .Append('[').Append((s.Factor != null) ? s.Factor.DescribeTerms() : "-").Append(']')
                      .Append(applied ? "/noTerrain=" : "/pred=").Append(Fmt(other));
                }
                else
                {
                    sb.Append(Fmt(s.Weight)).Append('/').Append(Fmt(s.T)).Append('/').Append(Fmt(other));
                }
            }
            return sb.ToString();
        }

        /// <summary>The option with the highest score with (or without) terrain; first on a tie, as native's MaxBy. Null with no option.</summary>
        private static TacticComponent Pick(List<OptionScore> scores, bool withTerrain)
        {
            TacticComponent best = null;
            float bestScore = float.NegativeInfinity;
            foreach (OptionScore s in scores)
            {
                float score = withTerrain ? s.With : s.Without;
                if (best == null || score > bestScore)
                {
                    best = s.Tactic;
                    bestScore = score;
                }
            }
            return best;
        }

        /// <summary>A TryDequeue of one of the AI-thread log queues.</summary>
        private delegate bool TryTake<T>(out T e);

        /// <summary>Main thread: drains one AI-thread queue (log off: drops it, so it never grows), one line per event with a team; flushes after writing when asked.</summary>
        private void Drain<T>(TryTake<T> take, Func<T, Team> team, Func<T, string> line, bool flush) where T : class
        {
            T e;
            bool wrote = false;
            while (take(out e))
            {
                if (_log == null || team(e) == null)
                {
                    continue;
                }
                Log(line(e));
                wrote = true;
            }
            if (wrote && flush)
            {
                Flush();
            }
        }

        /// <summary>REPICK lines: the re-picks the AI thread queued.</summary>
        private void LogRepicks()
        {
            Drain<TacticSelection.RepickEvent>(TacticSelection.TryDequeueRepick, e => e.Team, e =>
            {
                float ratio = (e.FromWeight > 0f) ? e.ToWeight / e.FromWeight : float.NaN;
                return "t=" + Fmt(e.Time) + "\tREPICK\t" + TeamName(e.Team) + "\t" + e.From + " -> " + e.To
                    + "\tw=" + Fmt(e.FromWeight) + "/" + Fmt(e.ToWeight) + "\tratio=" + Fmt(ratio)
                    + "\tswitched=" + (e.Switched ? 1 : 0) + "\tflankReapply=" + (e.FlankReapply ? 1 : 0)
                    + "\treason=" + (e.Reason ?? "terrain");
            }, false);
        }

        /// <summary>Main thread, at the publish (log on): one FLANKPLAN line for a side's flank plan.</summary>
        private void LogFlankPlan(TerrainFlankPlan.SidePlan plan, float now)
        {
            if (plan == null)
            {
                return;
            }
            Log("t=" + Fmt(now) + "\tFLANKPLAN\t" + plan.Side + "\t" + TerrainFlankPlan.Describe(plan));
        }

        /// <summary>Main thread, every tick: keeps the flank plan current (TerrainFlankPlan.Tick); FLANKPLAN / FLANKWATCH lines with the log on. Runs log on or off.</summary>
        private void TickFlankPlan(Mission mission)
        {
            BattleTerrain terrain = BattleTerrain.Current;
            if (terrain == null)
            {
                return;
            }
            try
            {
                float now = mission.CurrentTime;
                _flankLines.Clear();
                TerrainFlankPlan.Tick(mission, terrain, now, _flankLines);
                if (_flankLines.Count > 0 && _log != null)
                {
                    foreach (string line in _flankLines)
                    {
                        Log("t=" + Fmt(now) + "\t" + line);
                    }
                    Flush();
                }
            }
            catch (Exception e)
            {
                ReportOnce("flankPlanTick", e);
            }
        }

        /// <summary>Main thread, every tick: keeps the cavalry and archer-spot plans current (re-plan on a background task by timer, WorldPositions built here); replan lines with the log on. Runs log on or off.</summary>
        private void TickArmyPlans(Mission mission)
        {
            BattleTerrain terrain = BattleTerrain.Current;
            if (terrain == null)
            {
                return;
            }
            float now = mission.CurrentTime;
            _replanLines.Clear();
            try
            {
                TerrainArmyReplan.Tick(mission, terrain, now, _replanLines);
            }
            catch (Exception e)
            {
                ReportOnce("armyReplan", e);
            }
            try
            {
                TerrainApproach.Tick(mission, terrain, now, _replanLines);
            }
            catch (Exception e)
            {
                ReportOnce("approachReplan", e);
            }
            if (_replanLines.Count > 0 && _log != null)
            {
                foreach (string line in _replanLines)
                {
                    Log("t=" + Fmt(now) + "\t" + line);
                }
                Flush();
            }
        }

        /// <summary>BEHAVIOR / CHOKEHOLD bypassed / APPROACH use / FORMSHAPE / FLANKUSE lines: the terrain events the AI thread queued (TerrainLog).</summary>
        private void LogTerrainEvents()
        {
            Drain<TerrainLog.Event>(TerrainLog.TryDequeue, e => e.Team,
                e => "t=" + Fmt(e.Time) + "\t" + TerrainLog.Format(e, TeamName(e.Team)), true);
        }

        /// <summary>Main thread, every tick: the battle position's re-pick (TerrainBattlePosition.TickRepick, background re-score); BATTLEPOS repick line with the log on. Runs log on or off.</summary>
        private void RepickBattlePosition(Mission mission)
        {
            BattleTerrain terrain = BattleTerrain.Current;
            if (terrain == null || !TerrainBattlePosition.IsPublished)
            {
                return;
            }
            try
            {
                float now = mission.CurrentTime;
                TerrainBattlePosition.Plan before = TerrainBattlePosition.Published;
                string line = TerrainBattlePosition.TickRepick(mission, terrain, now);
                if (line != null && _log != null)
                {
                    Log("t=" + Fmt(now) + "\tBATTLEPOS\trepick\t" + BattleSideEnum.Defender + "\t" + line);
                    TerrainBattlePosition.Plan after = TerrainBattlePosition.Published;
                    if (after != null && after != before)
                    {
                        // the re-pick's position has its own reserve plug point
                        Log("t=" + Fmt(now) + "\tRESERVE\trepick\t" + BattleSideEnum.Defender + "\t" + TerrainBattlePosition.DescribePlug(after.Result, after));
                    }
                    Flush();
                }
            }
            catch (Exception e)
            {
                ReportOnce("battlePositionRepick", e);
            }
        }

        /// <summary>REACT lines: the reaction monitor's lines the AI thread queued.</summary>
        private void LogReactions()
        {
            Drain<ReactionMonitor.LogEvent>(ReactionMonitor.TryDequeue, e => e.Team,
                e => "t=" + Fmt(e.Time) + "\tREACT\t" + TeamName(e.Team) + "\t" + e.Text, true);
        }

        private const float CompositionLogSeconds = 10f;
        private const float CompositionLogChange = 0.05f;

        private sealed class CompositionLine
        {
            public ArmyComposition.Snapshot Snapshot;
            public string Signature;
            public float At;
        }

        /// <summary>Per team: the snapshot, roles signature and time last written as COMPOSITION. Main thread only.</summary>
        private readonly Dictionary<Team, CompositionLine> _compositionLogged = new Dictionary<Team, CompositionLine>();

        /// <summary>COMPOSITION per team every CompositionLogSeconds, sooner when a share or k moved CompositionLogChange since the last line, or at once on a
        /// token / main / anchor / line / infantry-leads change; then the COMPEVENT lines the AI thread queued.</summary>
        private void LogComposition(Mission mission)
        {
            if (_log == null)
            {
                DrainComposition();
                return;
            }
            try
            {
                float now = mission.CurrentTime;
                foreach (Team team in mission.Teams)
                {
                    if (team == null || team.Side == BattleSideEnum.None || team.QuerySystem == null || team.QuerySystem.MemberCount <= 0)
                    {
                        continue;
                    }
                    // main thread: only what the AI thread already cached (no snapshot build, no latch change)
                    ArmyComposition.Snapshot snap;
                    if (!ArmyComposition.TryPeek(team, out snap))
                    {
                        continue;
                    }
                    Formation main = snap.Main;
                    Formation anchor = main ?? snap.FootAnchor;
                    string mainName = (main != null) ? TerrainCavalry.FormationName(main) : "-";
                    string anchorName = (anchor != null) ? TerrainCavalry.FormationName(anchor) : "-";
                    string lineName = (snap.LineHolder != null) ? TerrainCavalry.FormationName(snap.LineHolder) : "-";
                    string tokens = ArmyComposition.DescribeTokens(snap);
                    string signature = tokens + "|" + mainName + "|" + anchorName + "|" + snap.AnchorReason + "|" + lineName + "|" + (snap.InfantryLeads ? "1" : "0");
                    CompositionLine last;
                    string trigger;
                    if (!_compositionLogged.TryGetValue(team, out last))
                    {
                        trigger = "first";
                    }
                    else if (signature != last.Signature)
                    {
                        trigger = "roles";
                    }
                    else if (last.Snapshot != snap && ArmyComposition.MaxChange(snap, last.Snapshot) >= CompositionLogChange)
                    {
                        trigger = "change";
                    }
                    else if (now - last.At >= CompositionLogSeconds && last.Snapshot != snap)
                    {
                        trigger = "timer";
                    }
                    else
                    {
                        continue;
                    }
                    _compositionLogged[team] = new CompositionLine { Snapshot = snap, Signature = signature, At = now };
                    Log("t=" + Fmt(now) + "\tCOMPOSITION\t" + TeamName(team)
                        + "\t" + ArmyComposition.Describe(snap)
                        + "\tmen=" + snap.OwnMen.ToString(CultureInfo.InvariantCulture)
                        + " inf:" + snap.InfantryMen.ToString(CultureInfo.InvariantCulture)
                        + " arc:" + snap.RangedMen.ToString(CultureInfo.InvariantCulture)
                        + " cav:" + snap.CavalryMen.ToString(CultureInfo.InvariantCulture)
                        + " ha:" + snap.HorseArcherMen.ToString(CultureInfo.InvariantCulture)
                        + "\tenemyMen=" + snap.EnemyMen.ToString(CultureInfo.InvariantCulture)
                        + " inf:" + snap.EnemyMeleeFootMen.ToString(CultureInfo.InvariantCulture)
                        + " arc:" + snap.EnemyRangedMen.ToString(CultureInfo.InvariantCulture)
                        + " cav:" + snap.EnemyMeleeCavalryMen.ToString(CultureInfo.InvariantCulture)
                        + " ha:" + snap.EnemyHorseArcherMen.ToString(CultureInfo.InvariantCulture)
                        + "\tdismounted=" + snap.OwnDismounted.ToString(CultureInfo.InvariantCulture) + "/" + snap.EnemyDismounted.ToString(CultureInfo.InvariantCulture)
                        + "\tstrength=" + Fmt(snap.OwnStrength) + "/" + Fmt(snap.EnemyStrength) + " start=" + Fmt(snap.StartEnemyStrength)
                        + "\ttoken=" + tokens
                        + "\tmainRule=" + (snap.InfantryLeads ? "leads" : "noLead") + " share=" + ArmyComposition.F2(snap.TeamInfantryShare)
                        + "\tmain=" + mainName
                        + "\tanchor=" + anchorName + " reason=" + snap.AnchorReason
                        + "\tline=" + lineName + " reason=" + snap.LineReason
                        + "\tkFoot=" + ArmyComposition.F2(snap.KFoot)
                        + " kCav=" + ArmyComposition.F2(snap.KCav)
                        + " kWeak=" + ArmyComposition.F2(snap.KWeak)
                        + " kMount=" + ArmyComposition.F2(snap.KMount)
                        + " kHA=" + ArmyComposition.F2(snap.KHA)
                        + " footRangedEngage=" + (snap.FootRangedEngage ? "1" : "0")
                        + "\tdry=" + snap.DryText
                        + "\tscale=" + snap.ScaleReason + (snap.Limited ? "(limited)" : "")
                        + "\ttrigger=" + trigger);
                }
            }
            catch (Exception e)
            {
                ReportOnce("composition", e);
            }
            DrainComposition();
        }

        /// <summary>COMPEVENT lines; with the log off the queue is still emptied (Drain discards).</summary>
        private void DrainComposition()
        {
            Drain<ArmyComposition.LogEvent>(ArmyComposition.TryDequeue, e => e.Team,
                e => "t=" + Fmt(e.Time) + "\tCOMPEVENT\t" + TeamName(e.Team) + "\t" + e.Kind + "\tformation=" + e.Formation + "\twhat=" + e.What + "\t" + e.Text, true);
        }

        /// <summary>The OUTLINES line (everything after the time): dot counts, per kind, work and ms.</summary>
        private static string OutlinesLine(BattleTerrain.OutlinesInfo info)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("OUTLINES\tdots=").Append(info.Dots.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append("\ttraced=").Append(info.Traced.ToString(CultureInfo.InvariantCulture));
            sb.Append("\tcapped=").Append(info.Capped.ToString(CultureInfo.InvariantCulture));
            sb.Append("\tkinds=");
            BattleTerrain.OutlineKind[] kinds = (BattleTerrain.OutlineKind[])Enum.GetValues(typeof(BattleTerrain.OutlineKind));
            for (int i = 0; i < kinds.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                }
                int count = (i < info.DotsByKind.Count) ? info.DotsByKind[i] : 0;
                sb.Append(kinds[i].ToString().ToLowerInvariant()).Append(':').Append(count.ToString(CultureInfo.InvariantCulture));
            }
            sb.Append("\tmaskCells=").Append(info.MaskCells.ToString(CultureInfo.InvariantCulture));
            sb.Append("\tedges=").Append(info.Edges.ToString(CultureInfo.InvariantCulture));
            sb.Append("\tspacing=").Append(Fmt(BattleTerrainRead.OutlineDotSpacing));
            sb.Append("\tmax=").Append(BattleTerrainRead.OutlineMaxDots.ToString(CultureInfo.InvariantCulture));
            sb.Append("\tms=").Append(Fmt(info.Ms));
            if (info.Error != null)
            {
                sb.Append("\terror=").Append(info.Error);
            }
            return sb.ToString();
        }

        private static string TeamName(Team team)
        {
            return team.Side.ToString() + "#" + team.TeamIndex.ToString(CultureInfo.InvariantCulture);
        }

        protected override void OnEndMission()
        {
            base.OnEndMission();
            Finish();
        }

        public override void OnRemoveBehavior()
        {
            Finish();
            base.OnRemoveBehavior();
        }

        private void Finish()
        {
            // a running background part is abandoned: its generation no longer matches
            _generation++;
            if (_pending != null)
            {
                // observe a late fault so it is not reported as an unobserved task exception
                _pending.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                _pending = null;
            }
            if (_log != null)
            {
                float now = 0f;
                try
                {
                    now = (Mission != null) ? Mission.CurrentTime : 0f;
                }
                catch
                {
                }
                WriteTacticSummaries(now);
                Log("# mission ended at t=" + Fmt(now) + (_terrainRead ? "" : "  (terrain never read: the battle ended before deployment)"));
                if (_log != null)
                {
                    _log.Close();
                    _log = null;
                }
            }
            // the snapshot, its grid and the tactic factors go with the mission
            if (_active)
            {
                BattleTerrain.Clear();
                TerrainTacticFactors.Clear();
                TerrainFlankPlan.Clear();
                TerrainSpots.Clear();
                TerrainCavalry.Clear();
                TerrainChokeHold.Clear();
                TerrainBattlePosition.Clear();
                TerrainHedgehog.Clear();
                TerrainApproach.Clear();
                TerrainShape.Clear();
                ReactionMonitor.Clear();
            }
            _active = false;
            _terrainGrid = null;
            _tacticWatches.Clear();
            _compositionLogged.Clear();
        }

        private static string LogFolderPath
        {
            get { return System.IO.Path.Combine(RBMConfig.Utilities.GetConfigFolderPath(), "logs", "terrain"); }
        }

        private void OpenLog()
        {
            try
            {
                System.IO.Directory.CreateDirectory(LogFolderPath);
                LogRetention.PruneOldest(LogFolderPath, "terrain_*.log");
                string path = System.IO.Path.Combine(LogFolderPath, "terrain_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".log");
                _log = BufferedLogWriter.Open(path);
            }
            catch
            {
                _log = null;
            }
        }

        private void Log(string line)
        {
            // a failed write only ends the log
            if (_log != null && !_log.WriteLine(line))
            {
                _log = null;
            }
        }

        private void Flush()
        {
            if (_log != null)
            {
                _log.Flush();
            }
        }

        private void ReportOnce(string key, Exception e)
        {
            if (_reportedFailures.Contains(key))
            {
                return;
            }
            _reportedFailures.Add(key);
            Log("# FAILED [" + key + "] " + e.GetType().Name + ": " + Ascii(e.Message));
        }

        /// <summary>Printable ASCII only (the log is ASCII); anything else becomes '?'.</summary>
        private static string Ascii(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            StringBuilder sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                sb.Append((c >= 32 && c <= 126) ? c : (c == '\t' ? ' ' : '?'));
            }
            return sb.ToString();
        }

        private static string Fmt(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>The log's opening lines; every line kind and field is documented in docs/terrain-log-format.md.</summary>
        private static string Header(Mission mission)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("# RBM battle terrain log -- the ground between the armies, read once after deployment.  scene=")
              .Append(Ascii(mission.SceneName)).Append("  t=").Append(Fmt(mission.CurrentTime)).Append("\n");
            sb.Append("# The data is BattleTerrain.Current, computed in every field battle with RBM AI on; this log only prints it.").Append("\n");
            sb.Append("# log opened ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            try
            {
                sb.Append("  RBMAI.dll built ").Append(System.IO.File.GetLastWriteTime(typeof(BattleTerrainLogic).Assembly.Location).ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch
            {
            }
            sb.Append("\n");
            sb.Append("# Tab separated, ASCII. Line kinds and fields: docs/terrain-log-format.md (RBM source tree);").Append("\n");
            sb.Append("# tools/BattleLogCheck/Check-BattleLogs.ps1 checks one battle's AI + terrain logs.").Append("\n");
            return sb.ToString().Replace("\n", Environment.NewLine).TrimEnd();
        }
    }
}
