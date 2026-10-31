using System.Globalization;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// A reaction task given by the team's ReactionMonitor (aiReactionsEnabled): the formation leaves its tactic's behaviors for one job and comes
    /// back when done. Registered on every field formation and DORMANT without a task: WeightFactor 0, so FindBestBehavior skips it entirely.
    ///
    /// Begin (the monitor, AI thread): WeightFactor 1, GetAiWeight LiveWeight 100 while the task lasts (above every holding behavior, CavalryCharge
    /// and native ProtectFlank). Every tactic re-apply calls AI.ResetBehaviorWeights, so ResetBehavior is overridden to keep the weight while a task
    /// is live. End sets WeightFactor 0: the formation falls back to whatever its tactic set.
    ///
    /// Modes (ReactionMonitor.ReactMode), each Approach -> Strike -> Fight:
    ///  - Help (R1, infantry): Move to the attacker's edge on the side we come from (HelpStandOff), facing it; Strike = ChargeToTarget within HelpStrikeGap.
    ///  - Hit (R1, cavalry) / Exploit (R4, cavalry): Move to a flank / rear stage point chosen by the monitor (StageFlankOut / StageRearOut beyond the edge,
    ///    re-read from its current pose); Strike on arrival within StageArrive, after StageMaxSeconds, or within StageStrikeGap.
    ///  - Intercept (R2): Move to the screen point between the victim and the riders (InterceptScreen off the victim's edge), facing them; Strike within
    ///    InterceptStrikeGap (or at once: the monitor's head-on fallback).
    ///  - Hunt (R3, enemy archers): cavalry Moves to a stage point beside / behind them (HuntFlankOut / HuntRearOut), Strike on arrival, after StageMaxSeconds
    ///    or within HuntStrikeGap; frontal fallback and near infantry ("direct") strike at once. Infantry hunters go Loose.
    ///  - Deter (R7 shadow, R3 on riding horse archers): as Intercept, Strike only within DeterStrikeGap; back to Approach beyond DeterReleaseGap out of
    ///    melee - never a chase.
    ///  Fight = Strike with the formation at least FightMeleeShare in melee.
    ///  A MOUNTED Strike (AI, field battle) rides FormedApproach first: a Move to the target's near edge until its go rule, then ChargeToTarget (TickStrikeApproach).
    ///  Hit / Exploit / Hunt: the monitor re-picks the stage side when the way crosses the target's front (SideChanged); the stage-time strike waits in the target's front lane.
    /// The Move point is the FRONT-centre: a copy of the cached median WorldPosition (or the target's) with ONE SetVec2MT, rebuilt only past CachedMovePoint's
    /// RebuildDistance and RebuildSeconds (native caches the order position by its exact Vec2); facing re-aimed after > ~10 degrees. Arrangement Line, cavalry FormOrderWide, once on activation.
    ///
    /// Ends (REACT end lines): targetGone, targetRetreat, insignificant, disengaged (Fight under DisengagedShare for DisengagedSeconds; Exploit: the target
    /// under ExploitOpenShare in melee), Hunt only: screened (significant enemy melee foot within HuntScreenGap of the archers) and notRanged, timeout (60 s,
    /// Intercept 45 s; Fight up to FightMaxSeconds), chaseTimeout (not in Fight: ChaseSeconds without closing ChaseMetres), EARLY (a mounted responder before the
/// main lines meet, Early): timeout 30 s / Fight 45 s, disengaged 4 s, chaseTimeout 10 s, cavalry leashes x0.6, reasons suffixed " early"; leash (not in Fight: foot 100 m defender /
    /// 150 m attacker from the main infantry, cavalry 250 m (defender 150 m) or 100 m past the enemy main line, a reserve / the main infantry beyond its home
    /// leash, the main infantry in Fight too at MainHelpFightLeashFactor x), reshuffled (not in Fight), armyRetreat, lostControl. The monitor adds relieved,
    /// threatened, overridden, replaced, noMen, sideClosed; a hunt on riding horse archers turns into a Deter (Redirect).
    ///
    /// THREADING: TickOccasionally / CalculateCurrentOrder / GetAiWeight run on the async AI thread, as does the monitor: cached formation data, managed
    /// melee-hit times, one SetVec2MT on a fresh copy; no engine query, no WorldPosition construction, no MBRandom. OnLostAIControl may come from the
    /// main thread: it only sets a volatile flag. StateName is a volatile string for the map view.
    /// </summary>
    internal class RBMBehaviorReact : BehaviorComponent
    {
        internal enum Phase
        {
            Approach,
            Strike,
            Fight
        }


        // Gaps are edge gaps in m, times in s; the class comment says how each is used.
        internal const float LiveWeight = 100f; // above every behavior a tactic gives (holding behaviors 20, x2 keep bonus)
        private const float HelpStandOff = 10f;
        private const float HelpStrikeGap = 25f;
        internal const float StageFlankOut = 60f; // Hit / Exploit stage point beyond the target's flank edge (the run-up)
        internal const float StageRearOut = 70f;
        private const float StageArrive = 20f; // from the stage point
        private const float StageMaxSeconds = 20f; // in Approach
        private const float StageStrikeGap = 50f;
        internal const float InterceptScreen = 25f; // screen point off the victim's edge toward the riders
        private const float InterceptStrikeGap = 80f;
        internal const float HuntFlankOut = 30f;
        internal const float HuntRearOut = 30f;
        private const float HuntStrikeGap = 60f;
        private const float HuntScreenGap = 30f; // significant enemy melee foot this close to the archers = screened
        internal const float DeterStrikeGap = 40f;
        internal const float DeterReleaseGap = 60f; // beyond this and out of melee: back to the screen (no chase)
        private const float TimeoutDeter = 60f;
        private const float FrontLaneMargin = 20f; // m + half width: the stage-time strike waits in the target's front lane
        private const float FightMeleeShare = 0.1f; // Strike -> Fight (HoldLateCharge.MeleeShare)
        private const float DisengagedShare = 0.05f;
        private const float DisengagedSeconds = 8f;
        private const float ExploitOpenShare = 0.1f; // target's melee share before contact: the opening closed ...
        private const float ExploitOpenSeconds = 6f; // ... after this long
        private const float TimeoutHelp = 60f;
        private const float TimeoutIntercept = 45f;
        private const float TimeoutExploit = 60f;
        private const float TimeoutHunt = 60f;
        private const float FightMaxSeconds = 90f; // in all
        private const float ChaseSeconds = 20f; // chaseTimeout: without closing ChaseMetres on the objective
        private const float ChaseMetres = 30f;
        private const float PointArrive = 15f; // m + a quarter of its depth of the Move point restarts the chase clock
        internal const float LeashFootDefender = 100f; // leash (not in Fight): from the main infantry
        internal const float LeashFootAttacker = 150f;
        internal const float LeashCavalryMain = 250f;
        internal const float LeashCavalryDefender = 150f; // a reserve keeps its 150 m home leash

        internal static float CavalryMainLeash(Team team)
        {
            // outmatched by the enemy's melee cavalry: a shorter leash (kWeak)
            float scale = 1f - ArmyComposition.WeakShrink * ArmyComposition.KWeak(team);
            return ((team != null && team.Side == BattleSideEnum.Defender) ? LeashCavalryDefender : LeashCavalryMain) * scale;
        }

        internal const float LeashCavalryBeyond = 100f; // past the enemy main line's rear

        // user rule 2026-10-30: before the main lines meet, mounted responders come back to their flank guard sooner
        private const float EarlyLinesMetGap = 40f; // edge gap main <-> enemy main; nearer (or the main in melee) = lines met
        private const float EarlyTimeout = 30f;
        private const float EarlyFightMaxSeconds = 45f;
        private const float EarlyDisengagedSeconds = 4f;
        private const float EarlyChaseSeconds = 10f;
        private const float EarlyLeashFactor = 0.6f;

        /// <summary>AI thread: a mounted responder while its army's main (else the position anchor) is out of melee and its enemy main still beyond EarlyLinesMetGap.</summary>
        private static bool Early(Mission mission, Formation formation)
        {
            if (!formation.QuerySystem.IsCavalryFormation && !formation.QuerySystem.IsRangedCavalryFormation)
            {
                return false;
            }
            Formation main = ReactionMonitor.MainInfantry(formation.Team) ?? ArmyComposition.PositionAnchor(formation.Team);
            if (main == null || main == formation || HoldLateCharge.MeleeShare(main) >= FightMeleeShare)
            {
                return false;
            }
            Formation enemyMain;
            Vec2 forward;
            return ReactionMonitor.ArmyForward(mission, formation.Team, main, out enemyMain, out forward) && enemyMain != null
                && ReactionMonitor.EdgeGap(main, enemyMain) > EarlyLinesMetGap;
        }
        private const float ReshuffleShare = 0.6f; // of the men it began with (not in Fight)
        private const float FacingTurnCos = 0.985f; // re-aim after a turn of ~10 deg
        internal const float OverriddenSeconds = 5f; // another behavior active this long ends the task

        private ReactionMonitor.ReactTask _task;
        private Phase _phase = Phase.Approach;
        private float _phaseSince = float.NaN;
        private readonly CachedMovePoint _target = new CachedMovePoint();
        private Vec2 _facing = Vec2.Invalid;
        private float _chaseSince = float.NaN;
        private float _chaseDistance;
        private float _lowMeleeSince = float.NaN;
        private float _openGoneSince = float.NaN;
        private float _notActiveSince = float.NaN;
        private volatile bool _lostControl;
        private volatile string _stateName = "idle";
        // mounted Strike's formed approach (FormedApproach); _approaching / _approachTarget are read from any thread
        private bool _go;
        private volatile bool _approaching;
        private volatile Formation _approachTarget;
        private float _approachSince;
        private float _farSince = float.NaN;
        private Vec2 _strikeFacing = Vec2.Invalid;
        private Formation _strikePointTarget;
        private readonly CachedMovePoint _strikePoint = new CachedMovePoint();

        /// <summary>Any thread: the target an AI formation rides at in a mounted Strike's formed approach (its order is a Move), else null.</summary>
        internal static Formation FormedApproachTarget(Formation formation)
        {
            RBMBehaviorReact react = (formation != null && formation.IsAIControlled && formation.AI != null) ? formation.AI.ActiveBehavior as RBMBehaviorReact : null;
            return (react != null && react._approaching) ? react._approachTarget : null;
        }

        private void ResetStrikeApproach()
        {
            _go = false;
            _approaching = false;
            _farSince = float.NaN;
            _strikePointTarget = null;
        }

        public RBMBehaviorReact(Formation formation)
            : base(formation)
        {
            _behaviorSide = formation.AI.Side;
            base.CurrentOrder = MovementOrder.MovementOrderStop;
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
        }

        /// <summary>Null = dormant. AI thread.</summary>
        internal ReactionMonitor.ReactTask Task => _task;

        internal Phase CurrentPhase => _phase;

        /// <summary>Map view text ("help approach", "intercept strike", ...). Any thread.</summary>
        internal string StateName => _stateName;

        /// <summary>Set from any thread; the monitor ends the task. AI thread reads.</summary>
        internal bool LostControl => _lostControl;

        /// <summary>AI thread, the monitor.</summary>
        internal void Begin(ReactionMonitor.ReactTask task, float now)
        {
            _task = task;
            _lostControl = false;
            _phase = task.StrikeAtOnce ? Phase.Strike : Phase.Approach;
            _phaseSince = now;
            _target.Clear();
            _facing = Vec2.Invalid;
            _chaseSince = float.NaN;
            _chaseDistance = 0f;
            _lowMeleeSince = float.NaN;
            _openGoneSince = float.NaN;
            _notActiveSince = float.NaN;
            ResetStrikeApproach();
            UpdateStateName();
            WeightFactor = 1f;
        }

        /// <summary>AI thread: weight 0, back to the tactic's behaviors; tells the monitor (REACT end line, cooldowns). Nothing without a task.</summary>
        internal void End(string reason, float now)
        {
            ReactionMonitor.ReactTask task = _task;
            if (task == null)
            {
                return;
            }
            _task = null;
            _target.Reset();
            ResetStrikeApproach();
            _stateName = "idle";
            WeightFactor = 0f;
            task.Owner?.TaskEnded(this, task, _phase, reason, now);
        }

        /// <summary>AI thread, the monitor: mode change in place (hunt on riding HA -> Deter); back to Approach with a fresh Move point and chase clock.</summary>
        internal void Redirect(ReactionMonitor.ReactMode mode, string why, float now)
        {
            ReactionMonitor.ReactTask task = _task;
            if (task == null || task.Mode == mode)
            {
                return;
            }
            ReactionMonitor.ReactMode old = task.Mode;
            task.Mode = mode;
            task.StrikeAtOnce = false;
            _phase = Phase.Approach;
            _phaseSince = now;
            _target.Reset();
            _facing = Vec2.Invalid;
            _chaseSince = float.NaN;
            _lowMeleeSince = float.NaN;
            ResetStrikeApproach();
            UpdateStateName();
            if (task.Owner != null)
            {
                task.Owner.LogPhase(task, base.Formation, old + "->" + mode, why, now);
            }
        }

        internal void SideChanged()
        {
            _target.Reset();
            _chaseSince = float.NaN;
        }

        /// <summary>A tactic re-apply (AI.ResetBehaviorWeights) must not drop a live task: keep the weight while it lasts.</summary>
        public override void ResetBehavior()
        {
            if (_task == null)
            {
                base.ResetBehavior();
            }
        }

        public override void OnLostAIControl()
        {
            // possibly the main thread (a player order): only a flag; the monitor ends the task on the AI thread
            if (_task != null)
            {
                _lostControl = true;
            }
            base.OnLostAIControl();
        }

        public override void TickOccasionally()
        {
            Mission mission = Mission.Current;
            if (_task == null || mission == null)
            {
                return;
            }
            float now = mission.CurrentTime;
            string end = CheckEnd(now);
            if (end != null)
            {
                End(end, now);
                return;
            }
            UpdatePhase(now);
            CalculateCurrentOrder();
            base.Formation.SetMovementOrder(base.CurrentOrder);
            base.Formation.SetFacingOrder(CurrentFacingOrder);
        }

        protected override void OnBehaviorActivatedAux()
        {
            Formation formation = base.Formation;
            if (_task == null || formation == null)
            {
                return;
            }
            CalculateCurrentOrder();
            formation.SetMovementOrder(base.CurrentOrder);
            formation.SetFacingOrder(CurrentFacingOrder);
            formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);
            if (!RBMAI.Utilities.IsRangedForArrangement(formation))
            {
                // foot hunting archers walks into their arrows: Loose
                bool looseHunt = _task.Mode == ReactionMonitor.ReactMode.Hunt && !formation.QuerySystem.IsCavalryFormation;
                formation.SetArrangementOrder(looseHunt ? ArrangementOrder.ArrangementOrderLoose : ArrangementOrder.ArrangementOrderLine);
            }
            if (formation.QuerySystem.IsCavalryFormation)
            {
                formation.SetFormOrder(FormOrder.FormOrderWide);
            }
        }

        /// <summary>AI thread (also the monitor's 1 s pass before the behavior is active): own end reasons, or null.</summary>
        internal string CheckEnd(float now)
        {
            ReactionMonitor.ReactTask task = _task;
            Formation formation = base.Formation;
            Mission mission = Mission.Current;
            if (task == null || formation == null || mission == null || formation.Team == null)
            {
                return null;
            }
            if (formation.CountOfUnits <= 0)
            {
                return "noMen";
            }
            if (mission.RetreatSide != BattleSideEnum.None && mission.RetreatSide == formation.Team.Side)
            {
                return "armyRetreat";
            }
            if (_lostControl || !formation.IsAIControlled)
            {
                return "lostControl";
            }
            Formation target = task.Target;
            if (target == null || target.CountOfUnits <= 0)
            {
                return "targetGone";
            }
            if (ReactionMonitor.IsRetreating(target))
            {
                return "targetRetreat";
            }
            if (!ReactionMonitor.TaskSignificant(task, target))
            {
                return "insignificant " + ThreatSignificance.Describe(task.Reactor, target);
            }
            bool fight = _phase == Phase.Fight;
            bool deter = task.Mode == ReactionMonitor.ReactMode.Deter;
            if (task.Kind == ReactionMonitor.ReactKind.R3 && !ReactionMonitor.IsHuntTarget(target))
            {
                return "notRanged";
            }
            if (task.Mode == ReactionMonitor.ReactMode.Hunt)
            {
                // their cover arrived (any phase): the hunt does not ride into the enemy infantry
                float screenGap;
                Formation screen = ReactionMonitor.ScreeningFoot(mission, formation.Team, formation, target, HuntScreenGap, out screenGap);
                if (screen != null)
                {
                    return "screened " + ThreatSignificance.Describe(formation, screen) + " gap=" + FormationGeometry.GapText(screenGap);
                }
            }
            if (!fight && (formation.CountOfUnits < ReshuffleShare * task.StartMen || formation.RepresentativeClass != task.StartClass))
            {
                return "reshuffled men=" + formation.CountOfUnits.ToString(CultureInfo.InvariantCulture) + "/" + task.StartMen.ToString(CultureInfo.InvariantCulture)
                    + " class=" + formation.RepresentativeClass;
            }
            bool early = Early(mission, formation);
            string earlyText = early ? " early" : "";
            float age = now - task.StartedAt;
            float limit = fight ? (early ? EarlyFightMaxSeconds : FightMaxSeconds) : (early ? MathF.Min(Timeout(task.Mode), EarlyTimeout) : Timeout(task.Mode));
            if (age > limit)
            {
                return "timeout " + ReactionMonitor.Fmt0(age) + "s" + earlyText;
            }
            if (fight && task.MainHelp)
            {
                // the main infantry keeps its (wider) home leash in Fight too
                string home = CheckHome(formation, task, ReactionMonitor.MainHelpFightLeashFactor);
                if (home != null)
                {
                    return home;
                }
            }
            if (fight && deter)
            {
                // a Deter screen leaves Fight for its screen (UpdatePhase), never a disengaged end
                return null;
            }
            if (fight)
            {
                float melee = HoldLateCharge.MeleeShare(formation);
                if (melee < DisengagedShare)
                {
                    if (float.IsNaN(_lowMeleeSince))
                    {
                        _lowMeleeSince = now;
                    }
                    else if (now - _lowMeleeSince >= (early ? EarlyDisengagedSeconds : DisengagedSeconds))
                    {
                        return "disengaged melee=" + ReactionMonitor.Fmt2(melee) + earlyText;
                    }
                }
                else
                {
                    _lowMeleeSince = float.NaN;
                }
                return null;
            }
            if (task.Mode == ReactionMonitor.ReactMode.Exploit)
            {
                float targetMelee = HoldLateCharge.MeleeShare(target);
                if (targetMelee < ExploitOpenShare)
                {
                    if (float.IsNaN(_openGoneSince))
                    {
                        _openGoneSince = now;
                    }
                    else if (now - _openGoneSince >= ExploitOpenSeconds)
                    {
                        return "disengaged target melee=" + ReactionMonitor.Fmt2(targetMelee);
                    }
                }
                else
                {
                    _openGoneSince = float.NaN;
                }
            }
            // a Deter screen striking is never a chase
            string chase = (deter && _phase != Phase.Approach) ? null : CheckChase(formation, task, now, early ? EarlyChaseSeconds : ChaseSeconds);
            if (chase != null)
            {
                return chase + earlyText;
            }
            string leash = CheckLeash(mission, formation, task, early ? EarlyLeashFactor : 1f);
            return (leash != null) ? leash + earlyText : null;
        }

        /// <summary>Reserve / main infantry: HomeLeash x factor m from where it stood, or null.</summary>
        private static string CheckHome(Formation formation, ReactionMonitor.ReactTask task, float factor)
        {
            if (float.IsNaN(task.HomeLeash) || !task.Home.IsValid)
            {
                return null;
            }
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(formation);
            if (!centre.IsValid)
            {
                return null;
            }
            float home = centre.Distance(task.Home);
            return (home > task.HomeLeash * factor) ? "leash home=" + ReactionMonitor.Fmt0(home) + "m" : null;
        }

        /// <summary>Not in Fight; standing at the Move point restarts it.</summary>
        private string CheckChase(Formation formation, ReactionMonitor.ReactTask task, float now, float chaseSeconds)
        {
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(formation);
            if (!centre.IsValid)
            {
                return null;
            }
            float distance;
            if (_phase == Phase.Approach && _target.Desired.IsValid)
            {
                Vec2 at = _target.Desired -(_facing.IsValid ? _facing : Vec2.Zero) * (formation.Depth * 0.5f);
                distance = centre.Distance(at);
                if (distance <= PointArrive + formation.Depth * 0.25f)
                {
                    // waiting at the point (an interceptor on its screen) is no chase
                    _chaseSince = now;
                    _chaseDistance = distance;
                    return null;
                }
            }
            else
            {
                distance = ReactionMonitor.EdgeGap(formation, task.Target);
            }
            if (float.IsNaN(_chaseSince) || _chaseDistance - distance >= ChaseMetres)
            {
                _chaseSince = now;
                _chaseDistance = distance;
                return null;
            }
            if (now - _chaseSince >= chaseSeconds)
            {
                return "chaseTimeout d=" + ReactionMonitor.Fmt0(distance) + "m (was " + ReactionMonitor.Fmt0(_chaseDistance) + "m " + ReactionMonitor.Fmt0(now - _chaseSince) + "s ago)";
            }
            return null;
        }

        private string CheckLeash(Mission mission, Formation formation, ReactionMonitor.ReactTask task, float cavalryLeashFactor)
        {
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(formation);
            if (!centre.IsValid)
            {
                return null;
            }
            string home = CheckHome(formation, task, 1f);
            if (home != null)
            {
                return home;
            }
            Formation main = ReactionMonitor.MainInfantry(formation.Team);
            // no melee-infantry main: the same leashes from the position anchor (archers), none without one
            string from = "main";
            if (main == null)
            {
                main = ArmyComposition.PositionAnchor(formation.Team);
                from = "anchor";
            }
            if (main == null || main == formation)
            {
                return null;
            }
            string leash = Leash(mission, formation, main, centre, cavalryLeashFactor);
            if (leash != null && from == "anchor")
            {
                ArmyComposition.Note(formation.Team, formation, ArmyComposition.KindGate, "reactLeash", "fromAnchor " + TerrainCavalry.FormationName(main) + " " + leash);
                return leash.Replace("leash main=", "leash anchor=");
            }
            return leash;
        }

        private string Leash(Mission mission, Formation formation, Formation main, Vec2 centre, float cavalryLeashFactor)
        {
            float gap = ReactionMonitor.EdgeGap(formation, main);
            if (formation.QuerySystem.IsCavalryFormation || formation.QuerySystem.IsRangedCavalryFormation)
            {
                float cavalryLeash = CavalryMainLeash(formation.Team) * cavalryLeashFactor;
                if (gap > cavalryLeash)
                {
                    float kWeak = ArmyComposition.KWeak(formation.Team);
                    return "leash main=" + FormationGeometry.GapText(gap)
                        + ((kWeak > 0f) ? " limit=" + ReactionMonitor.Fmt0(cavalryLeash) + "m kWeak=" + ArmyComposition.F2(kWeak) : "");
                }
                Formation enemyMain;
                Vec2 forward;
                if (ReactionMonitor.ArmyForward(mission, formation.Team, main, out enemyMain, out forward) && enemyMain != null)
                {
                    float beyond = (centre - RBMAI.Utilities.GetFormationCenter(enemyMain)).DotProduct(forward) - enemyMain.Depth * 0.5f;
                    if (beyond > LeashCavalryBeyond)
                    {
                        return "leash beyondEnemy=" + ReactionMonitor.Fmt0(beyond) + "m";
                    }
                }
                return null;
            }
            float limit = (formation.Team.Side == BattleSideEnum.Defender) ? LeashFootDefender : LeashFootAttacker;
            return (gap > limit) ? "leash main=" + FormationGeometry.GapText(gap) : null;
        }

        private void UpdatePhase(float now)
        {
            ReactionMonitor.ReactTask task = _task;
            Formation formation = base.Formation;
            if (task == null || formation == null || task.Target == null)
            {
                return;
            }
            float gap = ReactionMonitor.EdgeGap(formation, task.Target);
            switch (_phase)
            {
                case Phase.Approach:
                    {
                        string why = null;
                        switch (task.Mode)
                        {
                            case ReactionMonitor.ReactMode.Help:
                                if (gap <= HelpStrikeGap && !task.PushOnly)
                                {
                                    why = "gap=" + FormationGeometry.GapText(gap);
                                }
                                break;
                            case ReactionMonitor.ReactMode.Intercept:
                                if (gap <= InterceptStrikeGap)
                                {
                                    why = "gap=" + FormationGeometry.GapText(gap);
                                }
                                break;
                            case ReactionMonitor.ReactMode.Deter:
                                if (gap <= DeterStrikeGap)
                                {
                                    why = "gap=" + FormationGeometry.GapText(gap);
                                }
                                break;
                            default:
                                {
                                    Vec2 centre = RBMAI.Utilities.GetFormationCenter(formation);
                                    Vec2 stage = StagePoint(task);
                                    float strikeGap = (task.Mode == ReactionMonitor.ReactMode.Hunt) ? HuntStrikeGap : StageStrikeGap;
                                    if (gap <= strikeGap)
                                    {
                                        why = "gap=" + FormationGeometry.GapText(gap);
                                    }
                                    else if (centre.IsValid && stage.IsValid && centre.Distance(stage) <= StageArrive)
                                    {
                                        why = "staged " + task.Side.ToString().ToLowerInvariant();
                                    }
                                    else if (now - _phaseSince >= StageMaxSeconds && !ReactionMonitor.InFrontOf(task.Target, centre, FrontLaneMargin))
                                    {
                                        // not from the target's front lane (a charge from there hits its front)
                                        why = "stageTime " + ReactionMonitor.Fmt0(now - _phaseSince) + "s";
                                    }
                                    break;
                                }
                        }
                        if (why != null)
                        {
                            SetPhase(Phase.Strike, now, why);
                        }
                        break;
                    }
                case Phase.Strike:
                    {
                        float melee = HoldLateCharge.MeleeShare(formation);
                        if (melee >= FightMeleeShare)
                        {
                            SetPhase(Phase.Fight, now, "melee=" + ReactionMonitor.Fmt2(melee) + " gap=" + FormationGeometry.GapText(gap));
                        }
                        else if (task.Mode == ReactionMonitor.ReactMode.Deter && gap > DeterReleaseGap)
                        {
                            SetPhase(Phase.Approach, now, "release gap=" + FormationGeometry.GapText(gap));
                        }
                        break;
                    }
                case Phase.Fight:
                    {
                        if (task.Mode == ReactionMonitor.ReactMode.Deter && gap > DeterReleaseGap)
                        {
                            float melee = HoldLateCharge.MeleeShare(formation);
                            if (melee < DisengagedShare)
                            {
                                SetPhase(Phase.Approach, now, "release gap=" + FormationGeometry.GapText(gap) + " melee=" + ReactionMonitor.Fmt2(melee));
                            }
                        }
                        break;
                    }
            }
        }

        private void SetPhase(Phase phase, float now, string why)
        {
            Phase old = _phase;
            _phase = phase;
            _phaseSince = now;
            _chaseSince = float.NaN;
            ResetStrikeApproach();
            UpdateStateName();
            ReactionMonitor.ReactTask task = _task;
            if (task != null && task.Owner != null)
            {
                task.Owner.LogPhase(task, base.Formation, old + "->" + phase, why, now);
            }
        }

        private void UpdateStateName()
        {
            ReactionMonitor.ReactTask task = _task;
            _stateName = (task == null) ? "idle" : task.Mode.ToString().ToLowerInvariant() + " " + _phase.ToString().ToLowerInvariant()
                + ((task.Mode == ReactionMonitor.ReactMode.Hunt || task.Mode == ReactionMonitor.ReactMode.Deter) ? " " + TerrainCavalry.FormationName(task.Target) : "");
        }

        protected override void CalculateCurrentOrder()
        {
            ReactionMonitor.ReactTask task = _task;
            Formation formation = base.Formation;
            Mission mission = Mission.Current;
            if (task == null || formation == null || mission == null || formation.CountOfUnits <= 0 || task.Target == null || task.Target.CountOfUnits <= 0)
            {
                _approaching = false;
                base.CurrentOrder = MovementOrder.MovementOrderStop;
                CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                return;
            }
            float now = mission.CurrentTime;
            if (_phase != Phase.Approach)
            {
                if (_phase == Phase.Strike && TickStrikeApproach(mission, formation, task, now))
                {
                    return;
                }
                _approaching = false;
                base.CurrentOrder = MovementOrder.MovementOrderChargeToTarget(task.Target);
                CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                return;
            }
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(formation);
            Vec2 targetCentre = RBMAI.Utilities.GetFormationCenter(task.Target);
            if (!centre.IsValid || !targetCentre.IsValid)
            {
                base.CurrentOrder = MovementOrder.MovementOrderChargeToTarget(task.Target);
                CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                return;
            }
            Vec2 desired;
            Vec2 facing;
            switch (task.Mode)
            {
                case ReactionMonitor.ReactMode.Help:
                    {
                        Vec2 away = centre - targetCentre;
                        Vec2 dir = (away.LengthSquared > 0.01f) ? away.Normalized() : -task.Target.Direction;
                        desired = targetCentre + dir * (ReactionMonitor.ExtentToward(task.Target, dir) + formation.Depth * 0.5f + HelpStandOff);
                        facing = -dir;
                        break;
                    }
                case ReactionMonitor.ReactMode.Intercept:
                case ReactionMonitor.ReactMode.Deter:
                    {
                        Vec2 anchor = task.Owner != null ? task.Owner.VictimAnchor(task) : Vec2.Invalid;
                        if (!anchor.IsValid)
                        {
                            anchor = centre;
                        }
                        Vec2 toRiders = targetCentre - anchor;
                        Vec2 dir = (toRiders.LengthSquared > 0.01f) ? toRiders.Normalized() : formation.Direction;
                        float victimExtent = (task.VictimSpot == 0 && task.Victim != null) ? ReactionMonitor.ExtentToward(task.Victim, dir) : 0f;
                        desired = anchor + dir * (victimExtent + InterceptScreen + formation.Depth * 0.5f);
                        facing = dir;
                        break;
                    }
                default:
                    {
                        desired = StagePoint(task);
                        if (!desired.IsValid)
                        {
                            base.CurrentOrder = MovementOrder.MovementOrderChargeToTarget(task.Target);
                            CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                            return;
                        }
                        Vec2 toTarget = targetCentre - desired;
                        facing = (toTarget.LengthSquared > 0.01f) ? toTarget.Normalized() : formation.Direction;
                        break;
                    }
            }
            if (!_facing.IsValid || _facing.DotProduct(facing) < FacingTurnCos)
            {
                _facing = facing;
            }
            // the Move point is the FRONT-centre: native lays the ranks out behind the order position
            Vec2 front = desired + _facing * (formation.Depth * 0.5f);
            // own median, else the target's (keeps a held point when neither has one)
            WorldPosition point;
            if (!_target.TryGet(front, now, formation.CachedMedianPosition, task.Target.CachedMedianPosition, false, true, false, out point))
            {
                base.CurrentOrder = MovementOrder.MovementOrderChargeToTarget(task.Target);
                CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
                return;
            }
            base.CurrentOrder = MovementOrder.MovementOrderMove(point);
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(_facing);
        }

        // USER RULE 2026-10-30: a mounted Strike rides formed (Move to the target's near edge) until FormedApproach's go rule holds, as
        // CavalryCharge does; the gap is React's own edge gap. True = the Move is set, false = ChargeToTarget.
        private bool TickStrikeApproach(Mission mission, Formation formation, ReactionMonitor.ReactTask task, float now)
        {
            Formation target = task.Target;
            Footprint shape;
            if (!mission.IsFieldBattle || !formation.IsAIControlled
                || (!formation.QuerySystem.IsCavalryFormation && !formation.QuerySystem.IsRangedCavalryFormation)
                || !FormationGeometry.TryGet(target, FootprintMode.Current, out shape))
            {
                return false;
            }
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(formation);
            Vec2 targetCentre = RBMAI.Utilities.GetFormationCenter(target);
            if (!centre.IsValid || !targetCentre.IsValid)
            {
                return false;
            }
            float gap = FormedApproach.Gap(formation, target);
            bool melee = HoldLateCharge.MeleeShare(formation) >= HoldLateCharge.ContactMeleeShare;
            bool keepLost = false;
            if (_go)
            {
                if (gap <= FormedApproach.KeepDistance || melee)
                {
                    _farSince = float.NaN;
                    return false;
                }
                if (float.IsNaN(_farSince))
                {
                    _farSince = now;
                }
                if (now - _farSince < FormedApproach.KeepSeconds)
                {
                    return false;
                }
                _go = false;
                keepLost = true;
            }
            else
            {
                string why = FormedApproach.GoReason(formation, target, gap, centre, targetCentre, melee);
                if (why != null)
                {
                    StrikeGo(task, formation, why, now);
                    return false;
                }
            }
            Vec2 edge;
            Vec2 u;
            if (!FormedApproach.TryLeadEdge(formation, target, shape, centre, targetCentre, gap, out edge, out u))
            {
                StrikeGo(task, formation, "gap=0m", now);
                return false;
            }
            Vec2 aim = edge - centre;
            Vec2 facing = (aim.LengthSquared > 0.01f) ? aim.Normalized() : u;
            if (_strikePointTarget != target)
            {
                _strikePoint.Reset();
                _strikeFacing = facing;
            }
            else if (!_strikeFacing.IsValid || _strikeFacing.DotProduct(facing) < FormedApproach.FacingTurnCos)
            {
                _strikeFacing = facing;
            }
            WorldPosition point;
            if (!_strikePoint.TryGet(edge, now, target.CachedMedianPosition, formation.CachedMedianPosition, false, true, false, out point))
            {
                StrikeGo(task, formation, "noPoint", now);
                return false;
            }
            _strikePointTarget = target;
            base.CurrentOrder = MovementOrder.MovementOrderMove(point);
            CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(_strikeFacing);
            _approachTarget = target;
            if (!_approaching)
            {
                _approaching = true;
                _approachSince = now;
                task.Owner?.LogPhase(task, formation, "strike approach", "gap=" + FormationGeometry.GapText(gap) + (keepLost ? " keepLost" : ""), now);
            }
            return true;
        }

        private void StrikeGo(ReactionMonitor.ReactTask task, Formation formation, string why, float now)
        {
            bool wasApproaching = _approaching;
            _approaching = false;
            _go = true;
            _farSince = float.NaN;
            if (wasApproaching)
            {
                task.Owner?.LogPhase(task, formation, "strike go", why + " after " + ReactionMonitor.Fmt0(now - _approachSince) + "s", now);
            }
        }

        private static Vec2 StagePoint(ReactionMonitor.ReactTask task)
        {
            if (task.Mode == ReactionMonitor.ReactMode.Hunt)
            {
                return ReactionMonitor.SidePoint(task.Target, task.Side, HuntFlankOut, HuntRearOut);
            }
            return ReactionMonitor.SidePoint(task.Target, task.Side, StageFlankOut, StageRearOut);
        }

        /// <summary>AI thread, the monitor's pass.</summary>
        internal bool Overridden(float now)
        {
            Formation formation = base.Formation;
            if (_task == null || formation == null || formation.AI == null || formation.AI.ActiveBehavior == this)
            {
                _notActiveSince = float.NaN;
                return false;
            }
            if (float.IsNaN(_notActiveSince))
            {
                _notActiveSince = now;
                return false;
            }
            return now - _notActiveSince >= OverriddenSeconds;
        }

        private static float Timeout(ReactionMonitor.ReactMode mode)
        {
            switch (mode)
            {
                case ReactionMonitor.ReactMode.Intercept:
                    return TimeoutIntercept;
                case ReactionMonitor.ReactMode.Exploit:
                    return TimeoutExploit;
                case ReactionMonitor.ReactMode.Hunt:
                    return TimeoutHunt;
                case ReactionMonitor.ReactMode.Deter:
                    return TimeoutDeter;
                default:
                    return TimeoutHelp;
            }
        }

        public override TextObject GetBehaviorString()
        {
            return new TextObject("{=RBMReact01}reacting to the battle");
        }

        protected override float GetAiWeight()
        {
            return (_task != null && base.Formation.CountOfUnits > 0) ? LiveWeight : 0f;
        }
    }
}
