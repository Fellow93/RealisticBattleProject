using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// The AI army's reactions (toggle aiReactionsEnabled): ONE monitor per AI team checks the battle every CheckSeconds and
    /// gives a free formation ONE task, carried out by RBMBehaviorReact (dormant without a task). Kinds:
    /// R1 own formation attacked (never the main infantry's frontal clash or an active holding line; a line-answers
    /// handshake first), R2 enemy cavalry round a flank / at the archers / the rear (army frame: forward = toward the nearest
    /// significant enemy infantry), R3 hunt the enemy archers (Hunt.cs), R4 exploit the opening a player leaves (cavalry to the
    /// target's open flank/rear), R6 balance re-pick (Balance.cs), R7 shadow (Detect.cs: a Deter screen vs parked riders).
    /// Events are confirmed after ConfirmChecks checks, answered by at most one task, and "covered" when an own task (or an
    /// allied AI team's) already strikes the attacker. Victims are the formations of every team of our side; responders only
    /// our own team's AI formations. Big victims judge an attacker as if they had at most SignificanceCapMen men.
    /// Responders (Assign.cs): AI-controlled free formations (not general, foot ranged, main infantry, in melee, or an active
    /// hedgehog / choke Line / reserve plug); score = time to goal x class factor / power factor; priority
    /// R2 > R1 > R3 > R4 with per-kind task caps and cooldowns. Cavalry answers a mounted attacker only with MountedPowerMin x its power.
    /// Runs as a postfix on TeamAIComponent.Tick (TeamAIGeneral, field battles, after deployment) right after the tactic's own TickOccasionally.
    /// THREADING: AI thread only. Cached formation data, managed melee-hit times and the immutable terrain snapshot; velocities
    /// from the monitor's own position samples; no engine spatial query, no WorldPosition, no MBRandom. Per-team state is plain
    /// collections; monitors live in a ConcurrentDictionary cleared by MissionStartReset and at mission end. REACT log lines go
    /// through a ConcurrentQueue drained on the main thread by BattleTerrainLogic.
    /// </summary>
    internal sealed partial class ReactionMonitor
    {
        internal enum ReactKind
        {
            R1,
            R2,
            R3,
            R4,
            R7
        }

        internal enum ReactMode
        {
            Help,
            Hit,
            Intercept,
            Exploit,
            Hunt,
            Deter
        }

        internal enum StageSide
        {
            None,
            Left,
            Right,
            Rear
        }

        /// <summary>One task given to a formation (RBMBehaviorReact). Written by the monitor at Begin; AI thread.</summary>
        internal sealed class ReactTask
        {
            public ReactionMonitor Owner;
            public int Id;
            public ReactKind Kind;
            public ReactMode Mode;
            public Formation Target; // the attacker; R4 the locked enemy
            public Formation Victim; // R4: the one locked with the target
            public int VictimSpot; // 0 = the victim formation; 1 / 2 = the main line's left / right end (R2)
            public Formation Reactor; // significance of Target is judged against it (R1 / R2 the victim, R4 the main infantry)
            public StageSide Side;
            public bool StrikeAtOnce;
            public bool PushOnly; // R3 infantry vs horse archers: Help's Move push only, never a strike
            public float StartedAt;
            public int StartMen;
            public FormationClass StartClass;
            public Vec2 Home = Vec2.Invalid; // a reserve's home
            public float HomeLeash = float.NaN; // m; NaN = none
            public ReactionEvent Event;
            public int HaRunChecks; // hunt on HA: checks in a row the target rode at R3HaMaxSpeed or more (haRunning end)
            public bool MainHelp; // MainHelpReason: its home leash holds in Fight too
            public int SideBadChecks; // Approach: checks in a row the way to the stage side crossed the target's front (RecheckSide)
        }

        /// <summary>One watched event (kind, victim, attacker); AI thread.</summary>
        internal sealed class ReactionEvent
        {
            public int Id;
            public ReactKind Kind;
            public Formation Victim;
            public Formation Attacker;
            public int Spot;
            public Formation Reactor;
            public int Checks;
            public bool Confirmed;
            public bool Seen;
            public float FirstSeen;
            public float LastTrue;
            public string Trigger;
            public float Gap;
            public float Closing;
            public float Eta = float.NaN;
            public ReactTask Task;
            public string Answer; // "line" once a holding line's allyFighting latched for the victim (answered for good)
            public bool LineChecked;
            public Formation Line;
            public float LineSince = float.NaN;
            public bool NoneLogged;
            public bool CapLogged;
            public Formation CoveredBy; // own formation whose strike on the attacker covers the event (re-checked each check)
            public bool CoveredLogged;
            public int Tasks;
            public float Key; // R3's priority among hunts (lower first)
        }


        /// <summary>The monitor checks a team this often (s).</summary>
        internal const float CheckSeconds = 1f;

        private const int ConfirmChecks = 2; // in a row (R4: R4LockChecks)

        private const float CloseAfterSeconds = 10f; // R1 / R4: condition gone this long

        private const int VictimMinMen = 5; // R1; player formations too

        private const float R1MeleeGap = HoldLateCharge.ContactGap;

        private const float R1MeleeShare = 0.15f;

        private const float R1FootGap = 60f;

        private const float R1FootClosing = 1f;

        private const float R1CavalryGap = 150f;

        private const float R1CavalryClosing = 3f;

        private const float R1MainSideMargin = 10f; // m beyond the main's half width to the side (else behind its centre)

        private const float R1ReliefGap = 60f; // edge gap; relieved = no melee, beyond this, not closing

        private const float R1ReliefClosing = 1f;

        // User rule: riders straight in front of our army get no R1 / R2 / R7 answer unless they charge right into a formation (Detect.cs Aspect).
        private const float FrontalLateralMargin = 30f; // m beyond the main line's half width: in front within it, "round" beyond it

        private const float FrontalCavalryReactGap = 30f; // edge gap ...

        private const float FrontalCavalryChargeSpeed = 4f; // ... and closing speed (m/s) of a frontal charge that is still answered

        private const float FrontalLateralSpeed = 2f; // m/s outward across the front while not closing = riding round

        private const float FrontalLookAhead = 20f; // s; where a closing ride crosses our front plane, at most this far ahead

        private const float FrontalSkipLogSeconds = 10f; // one "skip frontalCavalry" line per riders this often

        private const float R2FrontPast = 10f; // "round" also = this far behind the main's front plane

        private const float R2Closing = 3f;

        private const float R2MaxEta = 30f;

        private const float R2MaxGap = 300f;

        private const float R2HorseArcherGap = 60f; // edge gap

        private const float R2AttackerMeleeMax = 0.2f; // more in melee = fighting, not riding round

        private const float R2ScreenRange = 40f; // archers in front are screened by own infantry this close to the riders' line

        private const float R2RelieveEta = 45f; // relieved: turned away (R2ReliefClosing) or ETA beyond this, for R2RelieveSeconds

        private const float R2ReliefClosing = 1f;

        private const float R2RelieveSeconds = 5f;

        private const float R4EnemyMelee = 0.25f;

        private const float R4EnemyMeleeKeep = 0.1f;

        private const float R4OwnMelee = 0.2f;

        private const float R4OwnGap = 30f;

        private const int R4LockChecks = 6;

        // R4 side points: beyond the target's flank / rear edge (the OPEN test).
        private const float R4FlankOut = 40f;

        private const float R4RearOut = 50f;

        // OPEN: no other significant enemy this close to the point ...
        private const float R4ClearPoint = 80f;

        private const float R4ClearPath = 40f; // ... or to the responder's line to it

        private const float R4CavalryTrap = 150f; // no significant enemy melee cavalry this close to the point or the responder

        private const float FrontMargin = 20f; // the way to a side point stays this far beyond the target's half width while in front of it

        private const float R4TeamCooldown = 45f;

        private const float R4TargetCooldown = 60f;

        private const float R4ChargeRange = 100f; // R4 skipped while an own CavalryCharge on the target is this close (edge gap)

        private const int FreeMinMen = 10;

        private const float FreeMeleeMax = 0.1f;

        private const float FreeEnemyGap = 50f; // another significant enemy this close = busy

        private const float ReserveLeash = 150f; // cavalry Reserve answers R1 / R2 only this close to where they stand

        /// <summary>CavalryCharge is free while its target is beyond this edge gap (or it reforms / is idle).</summary>
        private const float ChargeFreeTargetRange = 100f;

        private const float LineHandshakeSeconds = 3f;

        // class factors (x1 for like vs like)
        private const float ClassCavalryVsFoot = 0.8f;

        private const float ClassInfantryVsCavalry = 2f;

        private const float InfantryVsCavalryMelee = 0.3f; // infantry answers cavalry only once the riders are this much in melee

        private const float R2SideMatch = 0.7f; // score x this when the cavalry's ProtectFlank side is the riders' side

        private const float PowerFactorMin = 0.5f;

        private const float PowerFactorMax = 1.5f;

        private const float PowerReject = 0.5f; // responder under this share of the attacker's power: refused unless it and the victim reach PowerJoint

        private const float PowerJoint = 1.2f;

        private const float R1MaxTtaInfantry = 40f;

        private const float R1MaxTtaCavalry = 30f;

        private const float HelpMaxGap = 120f; // infantry helps only this close (edge gap) to the attacker

        private const float R2EtaMargin = 2f; // R2: reach the screen point no later than the riders' ETA - this ...

        private const float R2HeadOnSlack = 5f; // ... else head-on (ChargeToTarget at once) when reaching them within ETA + this, score x R2HeadOnFactor

        private const float R2HeadOnFactor = 1.5f;

        private const float R4MaxTta = 40f;

        private const float ArcherPriority = 1.5f; // R1: an archer victim's attacker counts this much more

        private const int MaxTasks = 2; // live R1 / R2 tasks per team (MaxExploitTasks: R4)

        private const int MaxExploitTasks = 1;

        private const float ResponderCooldown = 20f;

        private const float PairCooldown = 30f;

        private const int MaxQueuedLines = 4000; // REACT lines waiting for the main thread; more are dropped

        private const int MaxCheckErrors = 5; // a throwing check is skipped; this many in a row stop the team's monitor

        private const int SignificanceCapMen = 100; // R1/R2/R3/R7: a bigger victim judges an attacker as if it had this many men (SignificantToVictim)


        private static readonly ConcurrentDictionary<Team, ReactionMonitor> Monitors = new ConcurrentDictionary<Team, ReactionMonitor>();

        private static readonly Func<Team, ReactionMonitor> CreateMonitor = team => new ReactionMonitor(team);

        private static int _nextId;

        private readonly Team _team;
        private float _nextCheck = float.NegativeInfinity;
        private int _check;
        private int _errors;
        private readonly List<ReactionEvent> _events = new List<ReactionEvent>();
        private readonly List<RBMBehaviorReact> _active = new List<RBMBehaviorReact>();
        private readonly Dictionary<Formation, float> _responderCooldown = new Dictionary<Formation, float>();
        private readonly Dictionary<PairKey, float> _pairCooldown = new Dictionary<PairKey, float>();
        private readonly Dictionary<Formation, float> _exploitTargetCooldown = new Dictionary<Formation, float>();
        private float _exploitTeamCooldownUntil = float.NegativeInfinity;
        private readonly Dictionary<Formation, float> _melee = new Dictionary<Formation, float>();
        private readonly List<ReactionEvent> _pending = new List<ReactionEvent>();

        private ReactionMonitor(Team team)
        {
            _team = team;
        }

        private struct PairKey : IEquatable<PairKey>
        {
            public Formation Responder;
            public Formation Attacker;

            public bool Equals(PairKey other)
            {
                return Responder == other.Responder && Attacker == other.Attacker;
            }

            public override bool Equals(object obj)
            {
                return obj is PairKey && Equals((PairKey)obj);
            }

            public override int GetHashCode()
            {
                int a = (Responder != null) ? Responder.GetHashCode() : 0;
                int b = (Attacker != null) ? Attacker.GetHashCode() : 0;
                return (a * 397) ^ b;
            }
        }

        /// <summary>The team AI's tick, AI thread: the monitor's check every CheckSeconds.</summary>
        [HarmonyPatch(typeof(TeamAIComponent), "Tick")]
        internal static class Patch
        {
            private static void Postfix(TeamAIComponent __instance, Team ___Team)
            {
                if (!RBMConfig.RBMConfig.aiReactionsEnabled || ___Team == null || !(__instance is TeamAIGeneral))
                {
                    return;
                }
                Mission mission = Mission.Current;
                if (mission == null || mission.MissionTeamAIType != Mission.MissionTeamAITypeEnum.FieldBattle || !mission.AllowAiTicking
                    || mission.Mode == MissionMode.Deployment || !___Team.HasBots)
                {
                    return;
                }
                // A tiny side (ArmyScale) does not react.
                ArmyScale.SideScale scale = ArmyScale.For(___Team.Side);
                if (scale != null && scale.ReactionsOff)
                {
                    return;
                }
                ReactionMonitor monitor = Monitors.GetOrAdd(___Team, CreateMonitor);
                float now = mission.CurrentTime;
                if (now < monitor._nextCheck)
                {
                    return;
                }
                monitor._nextCheck = now + CheckSeconds;
                DecisionTrace.Frame traceFrame = DecisionTrace.Enter(DecisionTrace.Scope.React, ___Team);
                try
                {
                    monitor.Check(mission, now);
                    monitor._errors = 0;
                }
                catch (Exception e)
                {
                    // Never throw into the engine's AI tick: skip the check; MaxCheckErrors in a row stop the team.
                    monitor._errors++;
                    bool stop = monitor._errors >= MaxCheckErrors;
                    EnqueueLine(___Team, now, "error\tfailed=" + e.GetType().Name + ": " + Sanitize(e.Message) + "\tcount="
                        + monitor._errors.ToString(CultureInfo.InvariantCulture) + (stop ? "\tstopped" : "\tskipped"));
                    if (stop)
                    {
                        monitor._nextCheck = float.PositiveInfinity;
                    }
                }
                finally
                {
                    DecisionTrace.Exit(traceFrame);
                }
            }
        }

        /// <summary>Main thread: MissionStartReset and the mission end (BattleTerrainLogic): drops every monitor, pending balance re-pick (R6) and queued line.</summary>
        internal static void Clear()
        {
            Monitors.Clear();
            TacticSelection.ClearBalanceRepicks();
            LogEvent dropped;
            while (LogEvents.TryDequeue(out dropped))
            {
            }
            Interlocked.Exchange(ref _queued, 0);
        }

        /// <summary>AI thread: one check.</summary>
        private void Check(Mission mission, float now)
        {
            _check++;
            PruneActive();
            UpdateSamples(mission, now);
            _melee.Clear();
            for (int i = 0; i < _events.Count; i++)
            {
                _events[i].Seen = false;
            }
            // main = melee infantry only (frontal clash, R4, MainHelp, leashes); the frame stands on the position anchor without one
            Formation main = MainInfantry(_team);
            Formation frameMain = main ?? ArmyComposition.PositionAnchor(_team);
            Formation enemyMain = null;
            Vec2 forward = Vec2.Invalid;
            bool frame = frameMain != null && ArmyForward(mission, _team, frameMain, out enemyMain, out forward);
            _frameForward = frame ? forward : Vec2.Invalid;
            _frameMain = frame ? frameMain : null;
            _frameEnemyMain = frame ? enemyMain : null;
            // A side without terrain plans (ArmyScale) answers R1 / R2 only.
            ArmyScale.SideScale scale = ArmyScale.For(_team.Side);
            bool limited = scale != null && scale.ReactionsLimited;
            DetectR1(mission, main, now);
            if (frame)
            {
                DetectR2(mission, frameMain, forward, now);
                if (!limited)
                {
                    DetectR7(mission, frameMain, forward, now);
                }
            }
            if (!limited)
            {
                DetectR3(mission, main, now);
                if (main != null)
                {
                    DetectR4(mission, main, now);
                }
            }
            ExpireEvents(now);
            TickTasks(mission, now);
            Assign(mission, main, now);
            if (!limited)
            {
                CheckBalance(mission, now);
            }
        }

        private void PruneActive()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                RBMBehaviorReact react = _active[i];
                if (react == null || react.Task == null || react.Task.Owner != this)
                {
                    _active.RemoveAt(i);
                }
            }
        }

        /// <summary>HoldLateCharge.MeleeShare, cached per check.</summary>
        private float Melee(Formation formation)
        {
            float share;
            if (_melee.TryGetValue(formation, out share))
            {
                return share;
            }
            share = HoldLateCharge.MeleeShare(formation);
            _melee[formation] = share;
            return share;
        }

        // shared geometry (AI thread, cached formation data)

        /// <summary>max(width, depth) / 2.</summary>
        internal static float Radius(Formation formation)
        {
            return (formation == null) ? 0f : MathF.Max(formation.Width, formation.Depth) * 0.5f;
        }

        /// <summary>Footprint edge gap (FormationGeometry, scatter-aware: either may be charging); MaxValue without positions.</summary>
        internal static float EdgeGap(Formation a, Formation b)
        {
            return FormationGeometry.Gap(a, FootprintMode.ScatterAware, b, FootprintMode.ScatterAware);
        }

        /// <summary>Footprint distance from a point (0 inside); MaxValue without one.</summary>
        internal static float GapToPoint(Formation formation, Vec2 point)
        {
            Footprint shape;
            return FormationGeometry.TryGet(formation, FootprintMode.ScatterAware, out shape) ? FormationGeometry.GapToPoint(shape, point) : float.MaxValue;
        }

        /// <summary>Half extent of its footprint along dir (a stand-off off its edge); 0 without one.</summary>
        internal static float ExtentToward(Formation formation, Vec2 dir)
        {
            Footprint shape;
            return FormationGeometry.TryGet(formation, FootprintMode.ScatterAware, out shape) ? FormationGeometry.ExtentAlong(shape, dir) : 0f;
        }

        /// <summary>Footprint distance from the segment p-q (0 when it crosses it); MaxValue without one.</summary>
        internal static float GapToSegment(Formation formation, Vec2 p, Vec2 q)
        {
            Footprint shape;
            return FormationGeometry.TryGet(formation, FootprintMode.ScatterAware, out shape) ? FormationGeometry.GapToSegment(shape, p, q) : float.MaxValue;
        }

        internal static bool IsRetreating(Formation formation)
        {
            return formation != null && formation.GetReadonlyMovementOrderReference().OrderType == OrderType.Retreat;
        }

        /// <summary>
        /// AI thread: the team's main infantry (FormationCohesion.TeamMain: largest melee infantry, AI- or player-controlled,
        /// with hysteresis) or null. Only READ: FreeReason refuses player-controlled responders. aiOnly: the largest
        /// AI-controlled melee infantry only (R6's HoldLineFighting).
        /// </summary>
        internal static Formation MainInfantry(Team team, bool aiOnly = false)
        {
            if (team == null)
            {
                return null;
            }
            return aiOnly ? FormationCohesion.LargestMeleeInfantry(team, true) : FormationCohesion.TeamMain(team);
        }

        /// <summary>AI thread: an allied team's main infantry (largest melee infantry, player-controlled or not, FormationCohesion's min-men and power-share rule) for R1's frontal-clash rule, or null.</summary>
        private static Formation SideMain(Team team)
        {
            return FormationCohesion.LargestMeleeInfantry(team, false);
        }

        /// <summary>Our team or another of our side (the player's in an AI army, an allied AI team).</summary>
        private bool IsFriendlyTeam(Team team)
        {
            return team != null && (team == _team || (team.Side == _team.Side && !team.IsEnemyOf(_team)));
        }

        /// <summary>AI thread, R1 / R2 / R3 / R7: is the enemy worth a reaction for the victim? ThreatSignificance with the victim capped at SignificanceCapMen men (power scaled alike); the lines' own holding rules keep the full measure.</summary>
        internal static bool SignificantToVictim(Formation victim, Formation enemy)
        {
            if (ThreatSignificance.IsSignificant(victim, enemy))
            {
                return true;
            }
            if (victim == null || enemy == null || victim.CountOfUnits <= SignificanceCapMen)
            {
                return false;
            }
            int men = enemy.CountOfUnits;
            if (men >= Math.Max(ThreatSignificance.MinMen, (int)Math.Ceiling(ThreatSignificance.MenShare * SignificanceCapMen)))
            {
                return true;
            }
            float ourPower = victim.QuerySystem.FormationPower * SignificanceCapMen / victim.CountOfUnits;
            return ourPower > 0f && enemy.QuerySystem.FormationPower >= ThreatSignificance.PowerShare * ourPower;
        }

        /// <summary>The significance a task's target is judged by: R4 against the main infantry in full, the other kinds by SignificantToVictim.</summary>
        internal static bool TaskSignificant(ReactTask task, Formation target)
        {
            return (task.Kind == ReactKind.R4) ? ThreatSignificance.IsSignificant(task.Reactor, target) : SignificantToVictim(task.Reactor, target);
        }

        /// <summary>In the target's front lane (half width + margin).</summary>
        internal static bool InFrontOf(Formation target, Vec2 point, float margin)
        {
            Vec2 c = RBMAI.Utilities.GetFormationCenter(target);
            Vec2 d = target.Direction;
            if (!c.IsValid || !point.IsValid || d.LengthSquared < 0.01f)
            {
                return false;
            }
            d = d.Normalized();
            Vec2 rel = point - c;
            return rel.DotProduct(d) > target.Depth * 0.5f && MathF.Abs(rel.DotProduct(new Vec2(-d.y, d.x))) <= target.Width * 0.5f + margin;
        }

        /// <summary>AI thread: the live task of ANOTHER AI team of our side striking the attacker, or null (all teams' AI ticks run on one thread).</summary>
        private RBMBehaviorReact OtherTeamTaskOn(Formation attacker)
        {
            foreach (KeyValuePair<Team, ReactionMonitor> pair in Monitors)
            {
                ReactionMonitor other = pair.Value;
                if (other == null || other == this || !IsFriendlyTeam(pair.Key))
                {
                    continue;
                }
                RBMBehaviorReact on = other.TaskOn(attacker);
                if (on != null)
                {
                    return on;
                }
            }
            return null;
        }

        /// <summary>AI thread, the army frame: forward = from our main (melee infantry or position anchor) toward the nearest significant enemy infantry (enemyMain), else toward the nearest significant enemy formation (enemyMain null), else the main's facing. False without a direction.</summary>
        internal static bool ArmyForward(Mission mission, Team team, Formation main, out Formation enemyMain, out Vec2 forward)
        {
            enemyMain = null;
            forward = Vec2.Invalid;
            if (mission == null || team == null || main == null)
            {
                return false;
            }
            Vec2 mc = RBMAI.Utilities.GetFormationCenter(main);
            if (!mc.IsValid)
            {
                return false;
            }
            float best = float.MaxValue;
            // an enemy whose foot is only token groups has no infantry line to face (the any-class fallback below)
            bool footToken = ArmyComposition.EnemyFootTokenOnly(team);
            foreach (Team other in mission.Teams)
            {
                if (other == null || !other.IsEnemyOf(team) || footToken)
                {
                    continue;
                }
                foreach (Formation f in other.FormationsIncludingSpecialAndEmpty)
                {
                    if (f == null || f.CountOfUnits <= 0 || !f.QuerySystem.IsInfantryFormation || !ThreatSignificance.IsSignificant(main, f))
                    {
                        continue;
                    }
                    Vec2 c = RBMAI.Utilities.GetFormationCenter(f);
                    if (!c.IsValid)
                    {
                        continue;
                    }
                    float d = c.DistanceSquared(mc);
                    if (d < best && d > 1f)
                    {
                        best = d;
                        enemyMain = f;
                        forward = (c - mc).Normalized();
                    }
                }
            }
            // the any-class fallback only for an army without a melee-infantry main or vs an enemy with (almost) no melee foot
            if (enemyMain == null && (FormationCohesion.TeamMain(team) == null || ArmyComposition.KFoot(team) > 0f))
            {
                // an enemy without significant infantry: toward its nearest significant formation (enemyMain stays null)
                best = float.MaxValue;
                foreach (Team other in mission.Teams)
                {
                    if (other == null || !other.IsEnemyOf(team))
                    {
                        continue;
                    }
                    foreach (Formation f in other.FormationsIncludingSpecialAndEmpty)
                    {
                        if (f == null || f.CountOfUnits <= 0 || !ThreatSignificance.IsSignificant(main, f))
                        {
                            continue;
                        }
                        Vec2 c = RBMAI.Utilities.GetFormationCenter(f);
                        float d = c.IsValid ? c.DistanceSquared(mc) : float.MaxValue;
                        if (d < best && d > 1f)
                        {
                            best = d;
                            forward = (c - mc).Normalized();
                        }
                    }
                }
                if (forward.IsValid)
                {
                    return true;
                }
                Vec2 dir = main.Direction;
                if (dir.LengthSquared < 0.01f)
                {
                    return false;
                }
                forward = dir.Normalized();
            }
            else if (enemyMain == null)
            {
                Vec2 dir = main.Direction;
                if (dir.LengthSquared < 0.01f)
                {
                    return false;
                }
                forward = dir.Normalized();
            }
            return true;
        }

        /// <summary>A point beside the target from its CURRENT pose: Left / Right = flank edge + flankOut, a quarter of its depth back; Rear = rear edge + rearOut. Invalid without a pose.</summary>
        internal static Vec2 SidePoint(Formation target, StageSide side, float flankOut, float rearOut)
        {
            if (target == null || side == StageSide.None)
            {
                return Vec2.Invalid;
            }
            Vec2 c = RBMAI.Utilities.GetFormationCenter(target);
            Vec2 d = target.Direction;
            if (!c.IsValid || d.LengthSquared < 0.01f)
            {
                return Vec2.Invalid;
            }
            d = d.Normalized();
            Vec2 right = new Vec2(d.y, -d.x);
            switch (side)
            {
                case StageSide.Left:
                    return c - right * (target.Width * 0.5f + flankOut) - d * (target.Depth * 0.25f);
                case StageSide.Right:
                    return c + right * (target.Width * 0.5f + flankOut) - d * (target.Depth * 0.25f);
                default:
                    return c - d * (target.Depth * 0.5f + rearOut);
            }
        }

        private static Vec2 ClosestOnSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            Vec2 ab = b - a;
            float len = ab.LengthSquared;
            if (len < 0.0001f)
            {
                return a;
            }
            float t = (p - a).DotProduct(ab) / len;
            t = (t < 0f) ? 0f : (t > 1f) ? 1f : t;
            return a + ab * t;
        }

        private static bool CrossesFront(Vec2 a, Vec2 b, Formation target, float margin)
        {
            Vec2 c = RBMAI.Utilities.GetFormationCenter(target);
            Vec2 d = target.Direction;
            if (!c.IsValid || d.LengthSquared < 0.01f)
            {
                return false;
            }
            d = d.Normalized();
            Vec2 q = ClosestOnSegment(c, a, b);
            return (q - c).DotProduct(d) > 0f && q.Distance(c) <= target.Width * 0.5f + margin;
        }

        // log (AI thread writes, main thread drains)

        internal sealed class LogEvent
        {
            public Team Team;
            public float Time;
            public string Text;
        }

        private static readonly ConcurrentQueue<LogEvent> LogEvents = new ConcurrentQueue<LogEvent>();

        private static int _queued;

        private static void EnqueueLine(Team team, float time, string text)
        {
            if (!RBMConfig.RBMConfig.battleTerrainLoggingEnabled || team == null)
            {
                return;
            }
            if (Interlocked.Increment(ref _queued) > MaxQueuedLines)
            {
                Interlocked.Decrement(ref _queued);
                return;
            }
            LogEvents.Enqueue(new LogEvent { Team = team, Time = time, Text = text });
        }

        /// <summary>Main thread (terrain log).</summary>
        internal static bool TryDequeue(out LogEvent e)
        {
            if (LogEvents.TryDequeue(out e))
            {
                Interlocked.Decrement(ref _queued);
                return true;
            }
            return false;
        }

        private static bool Logging => RBMConfig.RBMConfig.battleTerrainLoggingEnabled;

        /// <summary>AI thread; only with the terrain log on.</summary>
        private void Log(ReactKind kind, string what, int id, Formation victim, int spot, Formation reactor, Formation attacker, Formation responder,
            string mode, float tta, float score, string why, float now)
        {
            if (DecisionTrace.Active)
            {
                DecisionTrace.Note(responder ?? victim, "React", kind + " " + what + " id=" + id.ToString(CultureInfo.InvariantCulture)
                    + (mode != null ? " mode=" + mode : ""), why);
            }
            if (!Logging)
            {
                return;
            }
            string text = kind + "\t" + what + "\tid=" + id.ToString(CultureInfo.InvariantCulture)
                + "\tvictim=" + VictimName(victim, spot)
                + "\tattacker=" + ThreatSignificance.Describe(reactor, attacker)
                + "\tresponder=" + ((responder != null) ? TerrainCavalry.FormationName(responder) : "-")
                + "\tmode=" + (mode ?? "-")
                + "\ttta=" + (float.IsNaN(tta) ? "-" : Fmt0(tta) + "s")
                + "\tscore=" + (float.IsNaN(score) ? "-" : Fmt1(score))
                + "\twhy=" + (why ?? "-");
            EnqueueLine(_team, now, text);
        }

        private void LogEventLine(ReactionEvent ev, string what, Formation responder, string mode, float tta, float score, string why, float now)
        {
            if ((!Logging && !DecisionTrace.Active) || ev == null)
            {
                return;
            }
            Log(ev.Kind, what, ev.Id, ev.Victim, ev.Spot, Reactor(ev), ev.Attacker, responder, mode, tta, score, why, now);
        }

        /// <summary>AI thread, RBMBehaviorReact.</summary>
        internal void LogPhase(ReactTask task, Formation responder, string what, string why, float now)
        {
            if ((!Logging && !DecisionTrace.Active) || task == null)
            {
                return;
            }
            Log(task.Kind, "phase", task.Id, task.Victim, task.VictimSpot, task.Reactor, task.Target, responder, task.Mode.ToString(), float.NaN, float.NaN,
                what + " " + why, now);
        }

        private static string VictimName(Formation victim, int spot)
        {
            string name = TerrainCavalry.FormationName(victim);
            return (spot == 1) ? name + "(leftEnd)" : (spot == 2) ? name + "(rightEnd)" : name;
        }

        /// <summary>ReactionEvent.Reactor, else the victim.</summary>
        private static Formation Reactor(ReactionEvent ev)
        {
            return ev.Reactor ?? ev.Victim;
        }

        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "-";
            }
            char[] chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] < 32 || chars[i] > 126 || chars[i] == '\t')
                {
                    chars[i] = ' ';
                }
            }
            return new string(chars);
        }

        internal static string Fmt0(float v)
        {
            return float.IsNaN(v) ? "-" : (v >= float.MaxValue * 0.5f) ? "inf" : v.ToString("0", CultureInfo.InvariantCulture);
        }

        internal static string Fmt1(float v)
        {
            return float.IsNaN(v) ? "-" : (v >= float.MaxValue * 0.5f) ? "inf" : v.ToString("0.0", CultureInfo.InvariantCulture);
        }

        internal static string Fmt2(float v)
        {
            return float.IsNaN(v) ? "-" : v.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
