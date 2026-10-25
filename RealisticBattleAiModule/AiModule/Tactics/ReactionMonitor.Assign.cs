using System;
using System.Globalization;
using System.Text;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>ReactionMonitor's answers (AI thread): live tasks' monitor-side ends, priority order, line-answers handshake, free filter, candidate scoring, Begin, and the bookkeeping when a task ends.</summary>
    internal sealed partial class ReactionMonitor
    {
        private struct Plan
        {
            public Formation Responder;
            public RBMBehaviorReact React;
            public ReactMode Mode;
            public StageSide Side;
            public bool StrikeAtOnce;
            public float Tta;
            public float Score;
            public Vec2 Home;
            public float HomeLeash;
            public bool MainHelp;
            public string Note;
        }

        private static readonly StageSide[] Sides = { StageSide.Left, StageSide.Right, StageSide.Rear };

        private static readonly Comparison<ReactionEvent> PriorityOrder = ComparePriority;

        // This check's army frame (set in Check).
        private Vec2 _frameForward = Vec2.Invalid;

        private Formation _frameMain;

        private Formation _frameEnemyMain; // ArmyForward: the nearest significant enemy infantry

        // live tasks

        /// <summary>The monitor-side ends of every live task, and the behavior's own while it is not active yet.</summary>
        private void TickTasks(Mission mission, float now)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                RBMBehaviorReact react = _active[i];
                ReactTask task = (react != null) ? react.Task : null;
                if (task == null || task.Owner != this)
                {
                    continue;
                }
                Formation formation = react.Formation;
                string why = null;
                if (formation != null && formation.CountOfUnits <= 0)
                {
                    // An emptied formation is never ticked again (Team.Tick skips it): its task would hold a slot forever.
                    why = "noMen";
                }
                else if (react.LostControl || formation == null || !formation.IsAIControlled)
                {
                    why = "lostControl";
                }
                else if (react.Overridden(now))
                {
                    BehaviorComponent active = formation.AI.ActiveBehavior;
                    why = "overridden by " + ((active != null) ? active.GetType().Name : "-");
                }
                else if (react.CurrentPhase != RBMBehaviorReact.Phase.Fight)
                {
                    why = Threatened(mission, formation, task);
                    if (why == null)
                    {
                        string running = HorseArchersRunning(task);
                        if (running != null)
                        {
                            // The hunted horse archers ride: a Deter screen instead of a chase, only while they really hurt the victim, else the hunt ends.
                            string notThreat = DeterNotThreat(task.Victim, task.Target);
                            if (notThreat != null)
                            {
                                why = running + " " + notThreat;
                            }
                            else
                            {
                                react.Redirect(ReactMode.Deter, running, now);
                            }
                        }
                        else
                        {
                            why = RecheckSide(mission, react, task, now);
                        }
                    }
                }
                if (why == null && formation.AI.ActiveBehavior != react)
                {
                    why = react.CheckEnd(now);
                }
                if (why != null)
                {
                    react.End(why, now);
                }
            }
        }

        private const int SideRecheckChecks = 2; // in a row, before a stage side re-pick

        /// <summary>Hit / Exploit / Hunt in Approach: the side is re-chosen by the task's own rule; none ends the task "sideClosed".</summary>
        private string RecheckSide(Mission mission, RBMBehaviorReact react, ReactTask task, float now)
        {
            if (task.Side == StageSide.None || react.CurrentPhase != RBMBehaviorReact.Phase.Approach
                || (task.Mode != ReactMode.Hit && task.Mode != ReactMode.Exploit && task.Mode != ReactMode.Hunt))
            {
                return null;
            }
            Formation r = react.Formation;
            Formation target = task.Target;
            if (r == null || target == null || target.CountOfUnits <= 0)
            {
                return null;
            }
            Vec2 rc = RBMAI.Utilities.GetFormationCenter(r);
            if (!rc.IsValid)
            {
                return null;
            }
            bool hunt = task.Mode == ReactMode.Hunt;
            Vec2 p = SidePoint(target, task.Side, hunt ? RBMBehaviorReact.HuntFlankOut : RBMBehaviorReact.StageFlankOut,
                hunt ? RBMBehaviorReact.HuntRearOut : RBMBehaviorReact.StageRearOut);
            if (p.IsValid && !CrossesFront(rc, p, target, FrontMargin))
            {
                task.SideBadChecks = 0;
                return null;
            }
            if (++task.SideBadChecks < SideRecheckChecks)
            {
                return null;
            }
            task.SideBadChecks = 0;
            Vec2 goal;
            string closed = null;
            StageSide side;
            if (task.Mode == ReactMode.Hit)
            {
                side = BestStageSide(r, target, out goal);
            }
            else if (task.Mode == ReactMode.Exploit)
            {
                side = OpenSide(mission, r, target, out goal, out closed);
            }
            else
            {
                side = HuntSide(mission, r, target, rc, out goal, out closed);
            }
            if (side == StageSide.None)
            {
                return "sideClosed " + task.Side.ToString().ToLowerInvariant() + ((closed != null) ? " (" + closed + ")" : "");
            }
            if (side != task.Side)
            {
                StageSide old = task.Side;
                task.Side = side;
                react.SideChanged();
                LogPhase(task, r, "side", old.ToString().ToLowerInvariant() + "->" + side.ToString().ToLowerInvariant(), now);
            }
            return null;
        }

        /// <summary>Before contact: another significant enemy closing within ThreatGap, or (cavalry responder) significant enemy melee cavalry within ThreatCavalryGap closing; else null.</summary>
        private string Threatened(Mission mission, Formation responder, ReactTask task)
        {
            Vec2 rc = RBMAI.Utilities.GetFormationCenter(responder);
            if (!rc.IsValid)
            {
                return null;
            }
            Footprint responderShape;
            if (!FormationGeometry.TryGet(responder, FootprintMode.ScatterAware, out responderShape))
            {
                return null;
            }
            bool cavalry = responder.QuerySystem.IsCavalryFormation;
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(_team))
                {
                    continue;
                }
                foreach (Formation f in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (f == null || f == task.Target || f.CountOfUnits <= 0 || IsRetreating(f))
                    {
                        continue;
                    }
                    Footprint shape;
                    if (!FormationGeometry.TryGet(f, FootprintMode.ScatterAware, out shape))
                    {
                        continue;
                    }
                    float gap = FormationGeometry.Gap(responderShape, shape);
                    if (gap > ThreatCavalryGap || !ThreatSignificance.IsSignificant(responder, f))
                    {
                        continue;
                    }
                    float closing = Closing(f, rc);
                    bool enemyCavalry = f.QuerySystem.IsCavalryFormation && !f.QuerySystem.IsRangedCavalryFormation;
                    if ((cavalry && enemyCavalry && closing >= ThreatCavalryClosing) || (gap <= ThreatGap && closing >= ThreatClosing))
                    {
                        return "threatened " + ThreatSignificance.Describe(responder, f) + " gap=" + FormationGeometry.Shadow(gap, OldGap(responder, f)) + " closing=" + Fmt1(closing) + "m/s";
                    }
                }
            }
            return null;
        }

        // threatened: another significant enemy within ThreatGap (edge gap) closing at ThreatClosing ...
        private const float ThreatGap = 40f;

        private const float ThreatClosing = 1f;

        // ... or (cavalry responders) significant enemy melee cavalry within ThreatCavalryGap closing at ThreatCavalryClosing.
        private const float ThreatCavalryGap = 80f;

        private const float ThreatCavalryClosing = 3f;

        private RBMBehaviorReact TaskOn(Formation attacker)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                RBMBehaviorReact react = _active[i];
                ReactTask task = (react != null) ? react.Task : null;
                if (task != null && task.Owner == this && task.Target == attacker)
                {
                    return react;
                }
            }
            return null;
        }

        // assignment

        /// <summary>Every confirmed, unanswered event in priority order gets the best free responder.</summary>
        private void Assign(Mission mission, Formation main, float now)
        {
            int tasks = 0;
            int exploits = 0;
            int hunts = 0;
            int shadows = 0;
            for (int i = 0; i < _active.Count; i++)
            {
                ReactTask task = (_active[i] != null) ? _active[i].Task : null;
                if (task == null || task.Owner != this)
                {
                    continue;
                }
                if (task.Kind == ReactKind.R4)
                {
                    exploits++;
                }
                else if (task.Kind == ReactKind.R3)
                {
                    hunts++;
                }
                else if (task.Kind == ReactKind.R7)
                {
                    shadows++;
                }
                else
                {
                    tasks++;
                }
            }
            _pending.Clear();
            for (int i = 0; i < _events.Count; i++)
            {
                ReactionEvent ev = _events[i];
                // A line's allyFighting answer holds only while its latch does (a recalled charge drops it): then the handshake re-runs.
                if (ev.Confirmed && ev.Task == null && ev.Answer == "line" && !LineAnswered(ev.Line, ev.Victim))
                {
                    LogEventLine(ev, "line", ev.Line, "allyFighting", float.NaN, float.NaN, "released line(" + TerrainCavalry.FormationName(ev.Line)
                        + ") no longer latched for the victim", now);
                    ev.Answer = null;
                    ev.Line = null;
                    ev.LineChecked = false;
                    ev.NoneLogged = false;
                }
                if (ev.Confirmed && ev.Task == null && ev.Answer == null)
                {
                    _pending.Add(ev);
                }
            }
            if (_pending.Count == 0)
            {
                return;
            }
            if (_pending.Count > 1)
            {
                _pending.Sort(PriorityOrder);
            }
            for (int i = 0; i < _pending.Count; i++)
            {
                ReactionEvent ev = _pending[i];
                // Covered: an own task already strikes the attacker.
                RBMBehaviorReact on = TaskOn(ev.Attacker);
                bool otherTeam = false;
                if (on == null)
                {
                    // Another AI team of our side already strikes it.
                    on = OtherTeamTaskOn(ev.Attacker);
                    otherTeam = on != null;
                }
                if (on != null)
                {
                    Covered(ev, on.Formation, (on.Task != null) ? on.Task.Mode.ToString() : "-",
                        (otherTeam ? "allyTeam " : "") + "task id=" + ((on.Task != null) ? on.Task.Id.ToString(CultureInfo.InvariantCulture) : "-"), now);
                    continue;
                }
                if (ev.Kind == ReactKind.R1 && !LineHandshake(mission, ev, now))
                {
                    continue;
                }
                if (ev.Kind == ReactKind.R4)
                {
                    float until;
                    if (exploits >= MaxExploitTasks || now < _exploitTeamCooldownUntil
                        || (_exploitTargetCooldown.TryGetValue(ev.Attacker, out until) && now < until))
                    {
                        continue;
                    }
                    Formation striker;
                    string busy = OwnStrikeOn(mission, ev.Attacker, out striker);
                    if (busy != null)
                    {
                        Covered(ev, striker, "-", busy, now);
                        continue;
                    }
                }
                else if (ev.Kind == ReactKind.R3)
                {
                    float until;
                    if (hunts >= MaxHuntTasks || now < _huntTeamCooldownUntil
                        || (_huntTargetCooldown.TryGetValue(ev.Attacker, out until) && now < until))
                    {
                        continue;
                    }
                    Formation striker;
                    string busy = OwnStrikeOn(mission, ev.Attacker, out striker);
                    if (busy != null)
                    {
                        Covered(ev, striker, "-", busy, now);
                        continue;
                    }
                }
                else if (ev.Kind == ReactKind.R7)
                {
                    if (shadows >= MaxShadowTasks)
                    {
                        continue;
                    }
                }
                else if (tasks >= MaxTasks)
                {
                    if (!ev.CapLogged)
                    {
                        ev.CapLogged = true;
                        LogEventLine(ev, "none", null, null, float.NaN, float.NaN, "cap tasks=" + tasks.ToString(CultureInfo.InvariantCulture), now);
                    }
                    continue;
                }
                Plan plan;
                string rejects;
                Formation already;
                if (!ChooseResponder(mission, main, ev, now, out plan, out rejects, out already))
                {
                    if (already != null)
                    {
                        Covered(ev, already, "CavalryCharge", "alreadyCharging", now);
                    }
                    else if (!ev.NoneLogged)
                    {
                        ev.NoneLogged = true;
                        LogEventLine(ev, "none", null, null, float.NaN, float.NaN, "noResponder " + (rejects ?? "-"), now);
                    }
                    continue;
                }
                bool replacedExploit = plan.React.Task != null && plan.React.Task.Kind == ReactKind.R4;
                bool replacedHunt = plan.React.Task != null && plan.React.Task.Kind == ReactKind.R3;
                bool replacedShadow = plan.React.Task != null && plan.React.Task.Kind == ReactKind.R7;
                Begin(plan, ev, now);
                if (ev.Kind == ReactKind.R4)
                {
                    exploits++;
                }
                else if (ev.Kind == ReactKind.R3)
                {
                    hunts++;
                }
                else if (ev.Kind == ReactKind.R7)
                {
                    shadows++;
                }
                else
                {
                    tasks++;
                }
                if (replacedExploit)
                {
                    exploits--;
                }
                if (replacedHunt)
                {
                    hunts--;
                }
                if (replacedShadow)
                {
                    shadows--;
                }
            }
        }

        /// <summary>The event is answered by another strike on its attacker this check; a COVERED line when that strike changed.</summary>
        private void Covered(ReactionEvent ev, Formation by, string mode, string why, float now)
        {
            if (ev.CoveredBy == by && ev.CoveredLogged)
            {
                return;
            }
            ev.CoveredBy = by;
            ev.CoveredLogged = true;
            LogEventLine(ev, "covered", by, mode, float.NaN, float.NaN, why, now);
        }

        private static int Rank(ReactKind kind)
        {
            switch (kind)
            {
                case ReactKind.R2:
                    return 0;
                case ReactKind.R1:
                    return 1;
                case ReactKind.R3:
                    return 2;
                case ReactKind.R7:
                    return 3;
                default:
                    return 4;
            }
        }

        /// <summary>R2 (by ETA) before R1 (archer victims first, then by the attacker's power) before R3 (by Key) before R7 (by gap) before R4.</summary>
        private static int ComparePriority(ReactionEvent a, ReactionEvent b)
        {
            int ra = Rank(a.Kind);
            int rb = Rank(b.Kind);
            if (ra != rb)
            {
                return ra.CompareTo(rb);
            }
            if (a.Kind == ReactKind.R2)
            {
                float ea = float.IsNaN(a.Eta) ? float.MaxValue : a.Eta;
                float eb = float.IsNaN(b.Eta) ? float.MaxValue : b.Eta;
                int c = ea.CompareTo(eb);
                if (c != 0)
                {
                    return c;
                }
            }
            else if (a.Kind == ReactKind.R1)
            {
                int c = R1Key(b).CompareTo(R1Key(a));
                if (c != 0)
                {
                    return c;
                }
            }
            else if (a.Kind == ReactKind.R3)
            {
                int c = a.Key.CompareTo(b.Key);
                if (c != 0)
                {
                    return c;
                }
            }
            else if (a.Kind == ReactKind.R7)
            {
                int c = a.Gap.CompareTo(b.Gap);
                if (c != 0)
                {
                    return c;
                }
            }
            return a.Id.CompareTo(b.Id);
        }

        private static float R1Key(ReactionEvent ev)
        {
            float power = (ev.Attacker != null) ? ev.Attacker.QuerySystem.FormationPower : 0f;
            return (ev.Victim != null && RBMAI.Utilities.IsRangedForArrangement(ev.Victim)) ? power * ArcherPriority : power;
        }

        // line answers (the allyFighting handshake)

        /// <summary>R1: true when a formation should be sent now. A holding line that will answer by allyFighting is waited for LineHandshakeSeconds; latched = answered (false); not latched by then = a formation is sent.</summary>
        private bool LineHandshake(Mission mission, ReactionEvent ev, float now)
        {
            if (!ev.LineChecked)
            {
                ev.LineChecked = true;
                ev.Line = FindAnsweringLine(mission, ev);
                if (ev.Line != null)
                {
                    ev.LineSince = now;
                    LogEventLine(ev, "line", ev.Line, "allyFighting", float.NaN, float.NaN, "expected line(" + TerrainCavalry.FormationName(ev.Line) + ", allyFighting)", now);
                }
            }
            if (ev.Line == null)
            {
                return true;
            }
            if (LineAnswered(ev.Line, ev.Victim))
            {
                ev.Answer = "line";
                LogEventLine(ev, "line", ev.Line, "allyFighting", float.NaN, float.NaN, "latched line(" + TerrainCavalry.FormationName(ev.Line) + ", allyFighting)", now);
                return false;
            }
            if (now - ev.LineSince < LineHandshakeSeconds)
            {
                return false;
            }
            LogEventLine(ev, "line", ev.Line, "allyFighting", float.NaN, float.NaN, "late line(" + TerrainCavalry.FormationName(ev.Line) + ") not latched after "
                + Fmt0(now - ev.LineSince) + "s: sending a formation", now);
            ev.Line = null;
            return true;
        }

        /// <summary>An own line that answers the event by its own allyFighting rule (HoldLateCharge.AllyFightingReason), not latched yet; else null.</summary>
        private Formation FindAnsweringLine(Mission mission, ReactionEvent ev)
        {
            Formation attacker = ev.Attacker;
            if (attacker == null || ev.Victim == null || attacker.QuerySystem.IsCavalryFormation || attacker.QuerySystem.IsRangedCavalryFormation)
            {
                return null;
            }
            foreach (Formation line in _team.FormationsIncludingSpecialAndEmpty)
            {
                if (line == null || line == ev.Victim || line.CountOfUnits <= 0 || line.AI == null)
                {
                    continue;
                }
                BehaviorComponent active = line.AI.ActiveBehavior;
                float range = -1f;
                RBMBehaviorHoldPosition position = active as RBMBehaviorHoldPosition;
                RBMBehaviorHoldChoke choke = active as RBMBehaviorHoldChoke;
                RBMBehaviorHedgehog hedgehog = active as RBMBehaviorHedgehog;
                if (position != null && position.CurrentRole == RBMBehaviorHoldPosition.Role.Line && !position.Engaged && !position.InQuiet)
                {
                    range = RBMBehaviorHoldPosition.AllyFightRange;
                }
                else if (choke != null && choke.CurrentRole == RBMBehaviorHoldChoke.Role.Line && !choke.Charged)
                {
                    range = HoldLateCharge.LateAllyFightGap;
                }
                else if (hedgehog != null && hedgehog.CurrentRole == RBMBehaviorHedgehog.Role.Infantry && !hedgehog.Engaged)
                {
                    float cavalryGap;
                    if (HoldLateCharge.NearestSignificantEnemyCavalry(mission, line, out cavalryGap) != null && cavalryGap <= HoldLateCharge.RangedCavalryWait)
                    {
                        continue;
                    }
                    range = HoldLateCharge.LateAllyFightGap;
                }
                if (range < 0f || EdgeGap(line, ev.Victim) > range || !ThreatSignificance.IsSignificant(line, attacker))
                {
                    continue;
                }
                return line;
            }
            return null;
        }

        /// <summary>The line's latched allyFighting engage / charge went to <paramref name="victim"/>.</summary>
        private static bool LineAnswered(Formation line, Formation victim)
        {
            if (line == null || line.AI == null || victim == null)
            {
                return false;
            }
            RBMBehaviorHoldPosition position = line.AI.GetBehavior<RBMBehaviorHoldPosition>();
            if (position != null && position.AllyAnswered == victim)
            {
                return true;
            }
            RBMBehaviorHoldChoke choke = line.AI.GetBehavior<RBMBehaviorHoldChoke>();
            if (choke != null && choke.AllyAnswered == victim)
            {
                return true;
            }
            RBMBehaviorHedgehog hedgehog = line.AI.GetBehavior<RBMBehaviorHedgehog>();
            return hedgehog != null && hedgehog.AllyAnswered == victim;
        }

        // responders

        /// <summary>The best free formation for the event (lowest score), or false with the rejected candidates ("f2:Cavalry=busy ...") in rejects; already = an own RBMBehaviorCavalryCharge is on the attacker (covered).</summary>
        private bool ChooseResponder(Mission mission, Formation main, ReactionEvent ev, float now, out Plan best, out string rejects, out Formation already)
        {
            best = default(Plan);
            rejects = null;
            already = null;
            bool found = false;
            StringBuilder sb = (ev.NoneLogged || !Logging) ? null : new StringBuilder();
            foreach (Formation r in _team.FormationsIncludingSpecialAndEmpty)
            {
                if (r == null || r.CountOfUnits <= 0)
                {
                    continue;
                }
                bool reserve;
                bool alreadyOn;
                RBMBehaviorReact react;
                string why = FreeReason(mission, main, ev, r, now, out reserve, out react, out alreadyOn);
                if (alreadyOn)
                {
                    already = r;
                    return false;
                }
                Plan plan = default(Plan);
                if (why == null)
                {
                    why = PlanFor(mission, main, ev, r, react, reserve, out plan);
                }
                if (why != null)
                {
                    if (sb != null && sb.Length < 400)
                    {
                        sb.Append(TerrainCavalry.FormationName(r)).Append('=').Append(why).Append(' ');
                    }
                    continue;
                }
                if (!found || plan.Score < best.Score)
                {
                    best = plan;
                    found = true;
                }
            }
            if (!found && sb != null)
            {
                rejects = (sb.Length > 0) ? sb.ToString().TrimEnd() : "noFormation";
            }
            return found;
        }

        /// <summary>Why the responder is not free for the event, or null.</summary>
        private string FreeReason(Mission mission, Formation main, ReactionEvent ev, Formation r, float now, out bool reserve,
            out RBMBehaviorReact react, out bool alreadyOn)
        {
            reserve = false;
            alreadyOn = false;
            react = null;
            // A hunt's victim is only shot at: cavalry under the arrows may go after the archers itself.
            if (r == ev.Victim && ev.Kind != ReactKind.R3)
            {
                return "victim";
            }
            if (!r.IsAIControlled)
            {
                return "player";
            }
            if (r.CountOfUnits < FreeMinMen)
            {
                return "fewMen";
            }
            if (r == _team.GeneralsFormation || r == _team.BodyGuardFormation)
            {
                return "general";
            }
            if (IsRetreating(r))
            {
                return "retreat";
            }
            if (r == main)
            {
                // The main infantry answers only its own archers attacked close by; a holding line is refused below by its behavior.
                string mainWhy = MainHelpReason(mission, ev, r);
                if (mainWhy != null)
                {
                    return "mainInfantry(" + mainWhy + ")";
                }
            }
            FormationQuerySystem q = r.QuerySystem;
            bool cavalry = q.IsCavalryFormation;
            if (q.IsRangedCavalryFormation)
            {
                return "horseArchers";
            }
            if (!cavalry && RBMAI.Utilities.IsRangedForArrangement(r))
            {
                return "ranged";
            }
            if (!cavalry && !q.IsInfantryFormation)
            {
                return "class";
            }
            if (ev.Kind == ReactKind.R4 && !cavalry)
            {
                return "notCavalry";
            }
            // User rule: the defender's riders go after enemy riders at most once per MountedTeamCooldown per team.
            if (cavalry && _team.Side == BattleSideEnum.Defender && IsMounted(ev.Attacker) && now < _mountedTeamCooldownUntil)
            {
                return "mountedCooldown(" + Fmt0(_mountedTeamCooldownUntil - now) + "s)";
            }
            if (r.AI == null)
            {
                return "noAI";
            }
            react = r.AI.GetBehavior<RBMBehaviorReact>();
            if (react == null)
            {
                return "noBehavior";
            }
            ReactTask live = react.Task;
            // An R3 / R7 / R4 task not yet in contact may be taken by a higher kind (R1 / R2 over all, R3 over R7 / R4, R7 over R4).
            if (live != null && !((live.Kind == ReactKind.R3 || live.Kind == ReactKind.R4 || live.Kind == ReactKind.R7) && Rank(ev.Kind) < Rank(live.Kind)
                && react.CurrentPhase != RBMBehaviorReact.Phase.Fight))
            {
                return "busy";
            }
            float until;
            if (_responderCooldown.TryGetValue(r, out until) && now < until)
            {
                return "cooldown";
            }
            if (_pairCooldown.TryGetValue(new PairKey { Responder = r, Attacker = ev.Attacker }, out until) && now < until)
            {
                return "pairCooldown";
            }
            BehaviorComponent active = r.AI.ActiveBehavior;
            if (active is RBMBehaviorHedgehog)
            {
                return "hedgehog";
            }
            // the wedge's charge is the tactic; its behavior defends itself
            if (active is RBMBehaviorEmbolon || IsTacticWedge(r))
            {
                return "wedge";
            }
            if (active is RBMBehaviorReservePlug)
            {
                return "reservePlug";
            }
            RBMBehaviorHoldChoke choke = active as RBMBehaviorHoldChoke;
            if (choke != null)
            {
                if (choke.CurrentRole != RBMBehaviorHoldChoke.Role.Reserve)
                {
                    return "choke" + choke.CurrentRole;
                }
                reserve = true;
            }
            RBMBehaviorHoldPosition position = active as RBMBehaviorHoldPosition;
            if (position != null)
            {
                if (position.CurrentRole != RBMBehaviorHoldPosition.Role.Reserve)
                {
                    return "hold" + position.CurrentRole;
                }
                reserve = true;
            }
            if (reserve && ev.Kind == ReactKind.R4)
            {
                return "reserve";
            }
            RBMBehaviorCavalryCharge charge = active as RBMBehaviorCavalryCharge;
            if (charge != null)
            {
                Formation target = charge.TargetFormation;
                if (target != null && target == ev.Attacker && target.CountOfUnits > 0)
                {
                    alreadyOn = true;
                    return "already";
                }
                if (!charge.IsReformingOrIdle && target != null && target.CountOfUnits > 0 && EdgeGap(r, target) <= ChargeFreeTargetRange)
                {
                    return "charging " + TerrainCavalry.FormationName(target);
                }
            }
            // Busy where it stands: another significant enemy close, or in melee (the costly test last).
            Vec2 rc = RBMAI.Utilities.GetFormationCenter(r);
            if (!rc.IsValid)
            {
                return "noPosition";
            }
            Footprint responderShape;
            if (!FormationGeometry.TryGet(r, FootprintMode.ScatterAware, out responderShape))
            {
                return "noPosition";
            }
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(_team))
                {
                    continue;
                }
                foreach (Formation f in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (f == null || f == ev.Attacker || f.CountOfUnits <= 0)
                    {
                        continue;
                    }
                    Footprint shape;
                    if (FormationGeometry.TryGet(f, FootprintMode.ScatterAware, out shape) && FormationGeometry.Gap(responderShape, shape) <= FreeEnemyGap
                        && ThreatSignificance.IsSignificant(r, f))
                    {
                        return "engaged " + TerrainCavalry.FormationName(f);
                    }
                }
            }
            if (Melee(r) >= FreeMeleeMax)
            {
                return "inMelee";
            }
            return null;
        }

        /// <summary>The free formation's answer to <paramref name="ev"/> (mode, goal, time, score), or why it cannot answer.</summary>
        private string PlanFor(Mission mission, Formation main, ReactionEvent ev, Formation r, RBMBehaviorReact react, bool reserve, out Plan plan)
        {
            plan = default(Plan);
            Formation attacker = ev.Attacker;
            Vec2 rc = RBMAI.Utilities.GetFormationCenter(r);
            Vec2 ac = RBMAI.Utilities.GetFormationCenter(attacker);
            if (!rc.IsValid || !ac.IsValid)
            {
                return "noPosition";
            }
            FormationQuerySystem q = r.QuerySystem;
            bool cavalry = q.IsCavalryFormation;
            bool attackerMounted = attacker.QuerySystem.IsCavalryFormation || attacker.QuerySystem.IsRangedCavalryFormation;
            float speed = MathF.Max(q.MovementSpeedMaximum, 0.5f);
            float classFactor;
            float tta;
            ReactMode mode;
            StageSide side = StageSide.None;
            bool strikeAtOnce = false;
            Vec2 goal;
            string note = null;
            switch (ev.Kind)
            {
                case ReactKind.R1:
                    if (!cavalry)
                    {
                        if (attackerMounted && Melee(attacker) < InfantryVsCavalryMelee)
                        {
                            return "infVsCavalry";
                        }
                        float gap = EdgeGap(r, attacker);
                        if (gap > HelpMaxGap)
                        {
                            return "far(gap=" + FormationGeometry.Shadow(gap, OldGap(r, attacker)) + ")";
                        }
                        tta = gap / speed;
                        if (tta > R1MaxTtaInfantry)
                        {
                            return "tta(" + Fmt0(tta) + "s)";
                        }
                        mode = ReactMode.Help;
                        classFactor = attackerMounted ? ClassInfantryVsCavalry : 1f;
                        goal = ac;
                    }
                    else
                    {
                        side = BestStageSide(r, attacker, out goal);
                        if (side == StageSide.None)
                        {
                            return "noSide";
                        }
                        tta = GapToPoint(r, goal) / speed;
                        if (tta > R1MaxTtaCavalry)
                        {
                            return "tta(" + Fmt0(tta) + "s)";
                        }
                        mode = ReactMode.Hit;
                        classFactor = attackerMounted ? 1f : ClassCavalryVsFoot;
                        note = side.ToString().ToLowerInvariant();
                    }
                    break;
                case ReactKind.R2:
                    {
                        if (!cavalry && Melee(attacker) < InfantryVsCavalryMelee)
                        {
                            return "infVsCavalry";
                        }
                        Vec2 anchor = VictimAnchorFor(ev.Victim, ev.Spot);
                        if (!anchor.IsValid)
                        {
                            return "noAnchor";
                        }
                        Vec2 toRiders = ac - anchor;
                        Vec2 dir = (toRiders.LengthSquared > 0.01f) ? toRiders.Normalized() : Vec2.Zero;
                        float victimExtent = (ev.Spot == 0) ? ExtentToward(ev.Victim, dir) : 0f;
                        goal = anchor + dir * (victimExtent + RBMBehaviorReact.InterceptScreen + r.Depth * 0.5f);
                        tta = GapToPoint(r, goal) / speed;
                        float eta = float.IsNaN(ev.Eta) ? float.MaxValue : ev.Eta;
                        classFactor = cavalry ? 1f : ClassInfantryVsCavalry;
                        if (tta > eta - R2EtaMargin)
                        {
                            float head = EdgeGap(r, attacker) / speed;
                            if (head > eta + R2HeadOnSlack)
                            {
                                return "late(" + Fmt0(tta) + "s>eta " + Fmt0(eta) + "s)";
                            }
                            strikeAtOnce = true;
                            tta = head;
                            classFactor *= R2HeadOnFactor;
                            note = "headOn";
                        }
                        if (r.AI.ActiveBehavior is BehaviorProtectFlank && _frameForward.IsValid && main != null)
                        {
                            Vec2 mc = RBMAI.Utilities.GetFormationCenter(main);
                            if (mc.IsValid)
                            {
                                float lateral = (ac - mc).DotProduct(new Vec2(-_frameForward.y, _frameForward.x));
                                FormationAI.BehaviorSide riders = (lateral > 0f) ? FormationAI.BehaviorSide.Left : FormationAI.BehaviorSide.Right;
                                if (r.AI.Side == riders)
                                {
                                    classFactor *= R2SideMatch;
                                    note = (note == null) ? "flankGuard" : note + " flankGuard";
                                }
                            }
                        }
                        mode = ReactMode.Intercept;
                        break;
                    }
                case ReactKind.R3:
                    {
                        bool deter;
                        string refused = PlanHunt(mission, r, attacker, ev.Victim, rc, cavalry, speed, out side, out strikeAtOnce, out goal, out tta, out classFactor,
                            out note, out deter);
                        if (refused != null)
                        {
                            return refused;
                        }
                        mode = deter ? ReactMode.Deter : ReactMode.Hunt;
                        break;
                    }
                case ReactKind.R7:
                    {
                        // A screen between the parked riders and their likely prey; strikes only when they come (Deter).
                        goal = DeterGoal(ev.Victim, ev.Spot, attacker, r);
                        if (!goal.IsValid)
                        {
                            return "noAnchor";
                        }
                        if (cavalry)
                        {
                            string trap = CavalryTrap(mission, r, attacker, rc, 0f, true);
                            if (trap != null)
                            {
                                return trap;
                            }
                        }
                        tta = GapToPoint(r, goal) / speed;
                        if (tta > R7MaxTta)
                        {
                            return "tta(" + Fmt0(tta) + "s)";
                        }
                        mode = ReactMode.Deter;
                        classFactor = cavalry ? 1f : ClassInfantryVsCavalry;
                        note = "shadow";
                        break;
                    }
                default:
                    {
                        string closed;
                        side = OpenSide(mission, r, attacker, out goal, out closed);
                        if (side == StageSide.None)
                        {
                            return closed ?? "closed";
                        }
                        tta = GapToPoint(r, goal) / speed;
                        if (tta > R4MaxTta)
                        {
                            return "tta(" + Fmt0(tta) + "s)";
                        }
                        mode = ReactMode.Exploit;
                        classFactor = attackerMounted ? 1f : ClassCavalryVsFoot;
                        note = side.ToString().ToLowerInvariant();
                        break;
                    }
            }
            if (reserve && rc.Distance(goal) > ReserveLeash)
            {
                return "reserveLeash(" + Fmt0(rc.Distance(goal)) + "m)";
            }
            // Never send a formation where the task's own leash (RBMBehaviorReact) would end it at once.
            Vec2 mainCentre = (main != null && main != r) ? RBMAI.Utilities.GetFormationCenter(main) : Vec2.Invalid;
            if (mainCentre.IsValid)
            {
                // the responder's orientation at the goal is unknown: its circle radius
                float rr = Radius(r);
                float goalGap = MathF.Max(0f, GapToPoint(main, goal) - rr);
                float leash = cavalry ? RBMBehaviorReact.CavalryMainLeash(_team)
                    : (_team.Side == BattleSideEnum.Defender) ? RBMBehaviorReact.LeashFootDefender : RBMBehaviorReact.LeashFootAttacker;
                if (goalGap > leash)
                {
                    float old = FormationGeometry.ShadowOn ? MathF.Max(0f, goal.Distance(mainCentre) - Radius(main) - rr) : float.NaN;
                    return "leash(gap=" + FormationGeometry.Shadow(goalGap, old) + ")";
                }
            }
            // A hunt's goal (behind the enemy line, where archers stand) must also stay inside the cavalry leash past the
            // enemy's main line.
            if (ev.Kind == ReactKind.R3 && cavalry && _frameEnemyMain != null && _frameForward.IsValid)
            {
                Vec2 emc = RBMAI.Utilities.GetFormationCenter(_frameEnemyMain);
                if (emc.IsValid)
                {
                    float beyond = (goal - emc).DotProduct(_frameForward) - _frameEnemyMain.Depth * 0.5f;
                    if (beyond > RBMBehaviorReact.LeashCavalryBeyond - R3BeyondMargin)
                    {
                        return "leashBeyond(" + Fmt0(beyond) + "m)";
                    }
                }
            }
            float responderPower = q.FormationPower;
            float attackerPower = attacker.QuerySystem.FormationPower;
            // User rule: riders answer riders only with real odds (no joint power with the victim); a weaker interceptor stays home.
            if (cavalry && attackerMounted && responderPower < MountedPowerMin * attackerPower)
            {
                return "weakVsMounted(p=" + Fmt2((attackerPower > 0f) ? responderPower / attackerPower : 0f) + ")";
            }
            // A hunt's victim is not fighting the archers: no joint power.
            float victimPower = (ev.Victim != null && ev.Kind != ReactKind.R3) ? ev.Victim.QuerySystem.FormationPower : 0f;
            if (responderPower < PowerReject * attackerPower && responderPower + victimPower < PowerJoint * attackerPower)
            {
                return "weak(" + Fmt0(responderPower) + "<" + Fmt0(attackerPower) + ")";
            }
            float powerFactor = (attackerPower > 0f) ? MBMath.ClampFloat(MathF.Sqrt(responderPower / attackerPower), PowerFactorMin, PowerFactorMax) : PowerFactorMax;
            bool mainHelp = r == main;
            plan.Responder = r;
            plan.React = react;
            plan.Mode = mode;
            plan.Side = side;
            plan.StrikeAtOnce = strikeAtOnce;
            plan.Tta = tta;
            plan.Score = MathF.Max(tta, 0.5f) * classFactor / powerFactor * (mainHelp ? MainHelpFactor : 1f);
            plan.Home = (reserve || mainHelp) ? rc : Vec2.Invalid;
            plan.HomeLeash = mainHelp ? MainHelpLeash : reserve ? ReserveLeash : float.NaN;
            plan.MainHelp = mainHelp;
            plan.Note = mainHelp ? ((note == null) ? "mainHelp" : note + " mainHelp") : note;
            return null;
        }

        // User rule: a cavalry responder answers a MOUNTED attacker only with this share of its power (no joint power with the victim).
        private const float MountedPowerMin = 0.8f;

        // User rule: the DEFENDER's cavalry starts a task against a mounted attacker at most once per MountedTeamCooldown per team ...
        private const float MountedTeamCooldown = 30f;

        private const float MountedTimeoutPairCooldown = 60f; // ... the same responder waits this long after a "timeout" end

        private float _mountedTeamCooldownUntil = float.NegativeInfinity; // MountedTeamCooldown

        private static bool IsMounted(Formation formation)
        {
            return formation != null && (formation.QuerySystem.IsCavalryFormation || formation.QuerySystem.IsRangedCavalryFormation);
        }

        private bool IsTacticWedge(Formation formation)
        {
            TeamAIComponent ai = _team.TeamAI;
            RBMTacticEmbolon embolon = (ai != null) ? TacticSelection.CurrentTacticRef(ai) as RBMTacticEmbolon : null;
            return embolon != null && embolon.Wedge == formation;
        }

        private const float R7MaxTta = 40f;

        private const int MaxShadowTasks = 1; // live R7 tasks per team

        // Main infantry helps its own foot ranged formation: victim within MainHelpVictimGap (edge gap), the attacker within MainHelpAttackerGap of the main, no other significant enemy melee foot within MainHelpFootClear of it.
        private const float MainHelpVictimGap = 60f;

        private const float MainHelpAttackerGap = 80f;

        private const float MainHelpFootClear = 80f;

        internal const float MainHelpLeash = 80f; // m from where it stood

        internal const float MainHelpFightLeashFactor = 1.5f; // in Fight

        private const float MainHelpFactor = 1.5f; // score x this: the main is the last resort

        /// <summary>Why the main infantry may NOT answer the event, or null (R1 / R2 on our foot ranged only). Holding lines are refused later by their behavior.</summary>
        private string MainHelpReason(Mission mission, ReactionEvent ev, Formation main)
        {
            if (ev.Kind != ReactKind.R1 && ev.Kind != ReactKind.R2)
            {
                return "kind";
            }
            Formation victim = ev.Victim;
            if (ev.Spot != 0 || victim == null || victim.Team != _team || victim.QuerySystem.IsCavalryFormation || victim.QuerySystem.IsRangedCavalryFormation
                || !RBMAI.Utilities.IsRangedForArrangement(victim))
            {
                return "notOwnArchers";
            }
            float victimGap = EdgeGap(main, victim);
            if (victimGap > MainHelpVictimGap)
            {
                return "farGap=" + FormationGeometry.Shadow(victimGap, OldGap(main, victim));
            }
            float attackerGap = EdgeGap(main, ev.Attacker);
            if (attackerGap > MainHelpAttackerGap)
            {
                return "attackerFarGap=" + FormationGeometry.Shadow(attackerGap, OldGap(main, ev.Attacker));
            }
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(_team))
                {
                    continue;
                }
                foreach (Formation f in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (f == null || f == ev.Attacker || f.CountOfUnits <= 0 || IsRetreating(f) || !IsMeleeFoot(f))
                    {
                        continue;
                    }
                    if (EdgeGap(main, f) <= MainHelpFootClear && ThreatSignificance.IsSignificant(main, f))
                    {
                        return "enemyFoot:" + TerrainCavalry.FormationName(f);
                    }
                }
            }
            return null;
        }

        /// <summary>Deter (R7, R3 on riding horse archers): the screen centre between the victim (its anchor, a line end for Spot 1 / 2) and the riders, InterceptScreen m off its edge; Invalid without one.</summary>
        private Vec2 DeterGoal(Formation victim, int spot, Formation riders, Formation responder)
        {
            Vec2 anchor = VictimAnchorFor(victim, spot);
            Vec2 ac = RBMAI.Utilities.GetFormationCenter(riders);
            if (!anchor.IsValid || !ac.IsValid)
            {
                return Vec2.Invalid;
            }
            Vec2 toRiders = ac - anchor;
            Vec2 dir = (toRiders.LengthSquared > 0.01f) ? toRiders.Normalized() : Vec2.Zero;
            float victimExtent = (spot == 0) ? ExtentToward(victim, dir) : 0f;
            return anchor + dir * (victimExtent + RBMBehaviorReact.InterceptScreen + responder.Depth * 0.5f);
        }

        /// <summary>Hit: the nearest of the attacker's left / right / rear stage points whose way from the responder does not pass in front of it and that is walkable; None without one.</summary>
        private static StageSide BestStageSide(Formation responder, Formation target, out Vec2 goal)
        {
            goal = Vec2.Invalid;
            Vec2 rc = RBMAI.Utilities.GetFormationCenter(responder);
            if (!rc.IsValid)
            {
                return StageSide.None;
            }
            BattleTerrain terrain = BattleTerrain.Current;
            StageSide best = StageSide.None;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < Sides.Length; i++)
            {
                Vec2 p = SidePoint(target, Sides[i], RBMBehaviorReact.StageFlankOut, RBMBehaviorReact.StageRearOut);
                if (!p.IsValid || CrossesFront(rc, p, target, FrontMargin) || (terrain != null && !terrain.IsWalkable(p)))
                {
                    continue;
                }
                float d = rc.Distance(p);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = Sides[i];
                    goal = p;
                }
            }
            return best;
        }

        /// <summary>R4: the nearest OPEN side of the target for the responder (OPEN test at R4FlankOut / R4RearOut; the stage point lies beyond it); None with the first side's reason in why.</summary>
        private StageSide OpenSide(Mission mission, Formation responder, Formation target, out Vec2 goal, out string why)
        {
            goal = Vec2.Invalid;
            why = null;
            Vec2 rc = RBMAI.Utilities.GetFormationCenter(responder);
            if (!rc.IsValid)
            {
                why = "noPosition";
                return StageSide.None;
            }
            // A trap: significant enemy melee cavalry near the responder closes every side.
            string trap = CavalryTrap(mission, responder, target, rc, 0f, true);
            if (trap != null)
            {
                why = trap;
                return StageSide.None;
            }
            BattleTerrain terrain = BattleTerrain.Current;
            StageSide best = StageSide.None;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < Sides.Length; i++)
            {
                StageSide side = Sides[i];
                Vec2 p = SidePoint(target, side, R4FlankOut, R4RearOut);
                string closed = null;
                if (!p.IsValid)
                {
                    closed = "noPose";
                }
                else if (terrain != null && !terrain.IsWalkable(p))
                {
                    closed = "notWalkable";
                }
                else if (TerrainCavalry.IsBadGround(p))
                {
                    closed = "badGround";
                }
                else if (CrossesFront(rc, p, target, FrontMargin))
                {
                    closed = "front";
                }
                else
                {
                    closed = EnemyNear(mission, responder, target, rc, p);
                }
                if (closed != null)
                {
                    if (why == null)
                    {
                        why = side.ToString().ToLowerInvariant() + ":" + closed;
                    }
                    else if (why.Length < 160)
                    {
                        why += "," + side.ToString().ToLowerInvariant() + ":" + closed;
                    }
                    continue;
                }
                float d = rc.Distance(p);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = side;
                }
            }
            if (best != StageSide.None)
            {
                // The stage point (the run-up) on the open side; the OPEN test was at the strike point.
                goal = SidePoint(target, best, RBMBehaviorReact.StageFlankOut, RBMBehaviorReact.StageRearOut);
                why = null;
            }
            return best;
        }

        /// <summary>OPEN: another enemy significant to the responder within R4ClearPoint m of the point or R4ClearPath m of the way; else a cavalry trap; else null.</summary>
        private string EnemyNear(Mission mission, Formation responder, Formation target, Vec2 rc, Vec2 p)
        {
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(_team))
                {
                    continue;
                }
                foreach (Formation f in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (f == null || f == target || f.CountOfUnits <= 0 || IsRetreating(f))
                    {
                        continue;
                    }
                    Vec2 fc = RBMAI.Utilities.GetFormationCenter(f);
                    if (!fc.IsValid || !ThreatSignificance.IsSignificant(responder, f))
                    {
                        continue;
                    }
                    if (GapToPoint(f, p) <= R4ClearPoint)
                    {
                        return "enemyNear " + TerrainCavalry.FormationName(f);
                    }
                    if (GapToSegment(f, rc, p) <= R4ClearPath)
                    {
                        return "enemyOnPath " + TerrainCavalry.FormationName(f);
                    }
                }
            }
            return CavalryTrap(mission, responder, target, p, 0f, false);
        }

        /// <summary>Significant (to the responder) enemy melee cavalry, not the target, within R4CavalryTrap m of <paramref name="at"/> (atResponder: of the responder's footprint; around: of that formation's footprint); else null.</summary>
        private string CavalryTrap(Mission mission, Formation responder, Formation target, Vec2 at, float atRadius, bool atResponder, Formation around = null)
        {
            Footprint responderShape = default(Footprint);
            Formation shaped = atResponder ? responder : around;
            bool useShape = shaped != null && FormationGeometry.TryGet(shaped, FootprintMode.ScatterAware, out responderShape);
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(_team))
                {
                    continue;
                }
                foreach (Formation f in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (f == null || f == target || f.CountOfUnits <= 0 || !f.QuerySystem.IsCavalryFormation || f.QuerySystem.IsRangedCavalryFormation || IsRetreating(f))
                    {
                        continue;
                    }
                    Footprint shape;
                    if (!FormationGeometry.TryGet(f, FootprintMode.ScatterAware, out shape))
                    {
                        continue;
                    }
                    float gap = useShape ? FormationGeometry.Gap(responderShape, shape) : FormationGeometry.GapToPoint(shape, at) - atRadius;
                    if (gap <= R4CavalryTrap && ThreatSignificance.IsSignificant(responder, f))
                    {
                        return "cavalryTrap " + TerrainCavalry.FormationName(f) + (atResponder ? "(responder)" : "");
                    }
                }
            }
            return null;
        }

        /// <summary>R4: our cavalry already charges <paramref name="enemy"/> close; the reason, else null.</summary>
        private string OwnStrikeOn(Mission mission, Formation enemy, out Formation striker)
        {
            striker = null;
            foreach (Formation f in _team.FormationsIncludingSpecialAndEmpty)
            {
                if (f == null || f.CountOfUnits <= 0 || f.AI == null)
                {
                    continue;
                }
                BehaviorComponent active = f.AI.ActiveBehavior;
                RBMBehaviorCavalryCharge charge = active as RBMBehaviorCavalryCharge;
                if (charge != null && charge.TargetFormation == enemy && EdgeGap(f, enemy) <= R4ChargeRange)
                {
                    striker = f;
                    return "alreadyCharging";
                }
            }
            return null;
        }

        /// <summary>Gives <paramref name="ev"/>'s task to the plan's formation (an R4 task it had ends "replaced"); an ASSIGN line.</summary>
        private void Begin(Plan plan, ReactionEvent ev, float now)
        {
            RBMBehaviorReact react = plan.React;
            Formation r = plan.Responder;
            if (react.Task != null)
            {
                react.End("replaced by " + ev.Kind + " id=" + ev.Id.ToString(CultureInfo.InvariantCulture), now);
            }
            BehaviorComponent before = (r.AI != null) ? r.AI.ActiveBehavior : null;
            ReactTask task = new ReactTask
            {
                Owner = this,
                Id = ev.Id,
                Kind = ev.Kind,
                Mode = plan.Mode,
                Target = ev.Attacker,
                Victim = ev.Victim,
                VictimSpot = ev.Spot,
                Reactor = Reactor(ev),
                Side = plan.Side,
                StrikeAtOnce = plan.StrikeAtOnce,
                StartedAt = now,
                StartMen = r.CountOfUnits,
                StartClass = r.RepresentativeClass,
                Home = plan.Home,
                HomeLeash = plan.HomeLeash,
                MainHelp = plan.MainHelp,
                Event = ev
            };
            react.Begin(task, now);
            ev.Task = task;
            ev.Tasks++;
            if (!_active.Contains(react))
            {
                _active.Add(react);
            }
            LogEventLine(ev, "assign", r, plan.Mode.ToString(), plan.Tta, plan.Score,
                (plan.Note ?? "-") + " from=" + ((before != null) ? before.GetType().Name : "-") + " men=" + r.CountOfUnits.ToString(CultureInfo.InvariantCulture), now);
        }

        /// <summary>AI thread, RBMBehaviorReact.End: cooldowns, the event freed for a new answer, an END line.</summary>
        internal void TaskEnded(RBMBehaviorReact react, ReactTask task, RBMBehaviorReact.Phase phase, string reason, float now)
        {
            Formation r = (react != null) ? react.Formation : null;
            // "relieved" is the attacker's doing: no responder cooldown, or a feint finds the interceptor resting.
            if (r != null && (reason == null || !reason.StartsWith("relieved")))
            {
                _responderCooldown[r] = now + ResponderCooldown;
                if (reason != null && (reason.StartsWith("chaseTimeout") || reason.StartsWith("leash")))
                {
                    _pairCooldown[new PairKey { Responder = r, Attacker = task.Target }] = now + PairCooldown;
                }
            }
            // User rule: the defender's riders after enemy riders get a team cooldown, and a "timeout" keeps the pair apart MountedTimeoutPairCooldown ("relieved"/"replaced": none).
            if (r != null && _team.Side == BattleSideEnum.Defender && r.QuerySystem.IsCavalryFormation && IsMounted(task.Target)
                && (reason == null || (!reason.StartsWith("relieved") && !reason.StartsWith("replaced"))))
            {
                _mountedTeamCooldownUntil = now + MountedTeamCooldown;
                if (reason != null && reason.StartsWith("timeout"))
                {
                    PairKey pair = new PairKey { Responder = r, Attacker = task.Target };
                    float until;
                    if (!_pairCooldown.TryGetValue(pair, out until) || until < now + MountedTimeoutPairCooldown)
                    {
                        _pairCooldown[pair] = now + MountedTimeoutPairCooldown;
                    }
                }
            }
            if (task.Kind == ReactKind.R4)
            {
                _exploitTeamCooldownUntil = now + R4TeamCooldown;
                if (task.Target != null)
                {
                    _exploitTargetCooldown[task.Target] = now + R4TargetCooldown;
                }
            }
            else if (task.Kind == ReactKind.R3)
            {
                _huntTeamCooldownUntil = now + R3TeamCooldown;
                if (task.Target != null)
                {
                    _huntTargetCooldown[task.Target] = now + R3TargetCooldown;
                }
            }
            ReactionEvent ev = task.Event;
            if (ev != null && ev.Task == task)
            {
                ev.Task = null;
            }
            if (Logging)
            {
                Log(task.Kind, "end", task.Id, task.Victim, task.VictimSpot, task.Reactor, task.Target, r, task.Mode.ToString(), float.NaN, float.NaN,
                    (reason ?? "-") + " phase=" + phase + " after=" + Fmt0(now - task.StartedAt) + "s", now);
            }
        }

        /// <summary>AI thread, RBMBehaviorReact (Intercept): where the victim stands (cached order position while it moves away, or a line end point), or Invalid.</summary>
        internal Vec2 VictimAnchor(ReactTask task)
        {
            return (task == null) ? Vec2.Invalid : VictimAnchorFor(task.Victim, task.VictimSpot);
        }

        private Vec2 VictimAnchorFor(Formation victim, int spot)
        {
            if (victim == null || victim.CountOfUnits <= 0)
            {
                return Vec2.Invalid;
            }
            Vec2 vc = RBMAI.Utilities.GetFormationCenter(victim);
            if (!vc.IsValid)
            {
                return Vec2.Invalid;
            }
            if (spot == 1 || spot == 2)
            {
                Vec2 forward = _frameForward.IsValid ? _frameForward : victim.Direction;
                if (forward.LengthSquared < 0.01f)
                {
                    return vc;
                }
                forward = forward.Normalized();
                Vec2 left = new Vec2(-forward.y, forward.x);
                return vc + left * ((spot == 1 ? 1f : -1f) * victim.Width * 0.5f);
            }
            // Ranged walking away (ArcherCover, a new post): screen where it is going, the cached order position.
            if (RBMAI.Utilities.IsRangedForArrangement(victim) && victim.OrderPositionIsValid
                && victim.GetReadonlyMovementOrderReference().OrderType == OrderType.Move)
            {
                Vec2 order = victim.OrderPosition;
                if (order.IsValid && order.DistanceSquared(vc) > 100f)
                {
                    return order;
                }
            }
            return vc;
        }
    }
}
