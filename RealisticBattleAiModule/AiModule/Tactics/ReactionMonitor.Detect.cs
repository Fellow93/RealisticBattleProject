using System.Collections.Generic;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>ReactionMonitor's detection (AI thread): position samples per enemy formation, the R1 / R2 / R4 / R7 detectors (R3 is in Hunt.cs), the event table with hysteresis (strict condition to confirm, looser keep condition) and the expiry.</summary>
    internal sealed partial class ReactionMonitor
    {
        /// <summary>Every enemy formation's shared motion sample (EnemyMotion) taken this check.</summary>
        private void UpdateSamples(Mission mission, float now)
        {
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(_team))
                {
                    continue;
                }
                foreach (Formation f in team.FormationsIncludingSpecialAndEmpty)
                {
                    EnemyMotion.Touch(f, now);
                }
            }
        }

        /// <summary>The sampled speed (m/s) of <paramref name="enemy"/> toward <paramref name="point"/>; 0 without a velocity.</summary>
        private static float Closing(Formation enemy, Vec2 point)
        {
            return EnemyMotion.ClosingSpeed(enemy, point);
        }

        private ReactionEvent Find(ReactKind kind, Formation victim, Formation attacker, int spot)
        {
            for (int i = 0; i < _events.Count; i++)
            {
                ReactionEvent ev = _events[i];
                // R4 / R3 / R7 are keyed on the enemy alone: the own formation locked with it / shot at / nearest may change.
                if (ev.Kind == kind && ev.Attacker == attacker
                    && (kind == ReactKind.R4 || kind == ReactKind.R3 || kind == ReactKind.R7 || (ev.Victim == victim && ev.Spot == spot)))
                {
                    return ev;
                }
            }
            return null;
        }

        /// <summary>One detector hit: strict starts / confirms an event (after ConfirmChecks strict checks in a row), keep is the looser condition that keeps a confirmed one alive. Returns the event when this check counted for it, else null.</summary>
        private ReactionEvent Observe(ReactKind kind, Formation victim, int spot, Formation reactor, Formation attacker, bool strict, bool keep, string trigger,
            float gap, float closing, float eta, float now)
        {
            ReactionEvent ev = Find(kind, victim, attacker, spot);
            if (ev == null)
            {
                if (!strict)
                {
                    return null;
                }
                ev = new ReactionEvent
                {
                    Id = System.Threading.Interlocked.Increment(ref _nextId),
                    Kind = kind,
                    Victim = victim,
                    Attacker = attacker,
                    Spot = spot,
                    Reactor = reactor,
                    FirstSeen = now
                };
                _events.Add(ev);
            }
            bool ok = ev.Confirmed ? (strict || keep) : strict;
            if (!ok)
            {
                return null;
            }
            ev.Seen = true;
            ev.Checks++;
            ev.LastTrue = now;
            ev.Trigger = trigger;
            ev.Gap = gap;
            ev.Closing = closing;
            ev.Eta = eta;
            if (kind == ReactKind.R4)
            {
                ev.Victim = victim;
            }
            else if (kind == ReactKind.R3 || kind == ReactKind.R7)
            {
                // The archers may shoot at another formation now / the parked riders be nearest another: judged against that one.
                ev.Victim = victim;
                ev.Reactor = reactor;
                ev.Spot = spot;
            }
            int need = (kind == ReactKind.R4) ? R4LockChecks : (kind == ReactKind.R3) ? R3ConfirmChecks : (kind == ReactKind.R7) ? R7ConfirmChecks : ConfirmChecks;
            if (!ev.Confirmed && ev.Checks >= need)
            {
                ev.Confirmed = true;
                LogEventLine(ev, "detect", null, null, float.NaN, float.NaN, DetectWhy(ev), now);
            }
            return ev;
        }

        private static string DetectWhy(ReactionEvent ev)
        {
            string text = (ev.Trigger ?? "-") + " gap=" + FormationGeometry.GapText(ev.Gap) + " closing=" + Fmt1(ev.Closing) + "m/s";
            if (!float.IsNaN(ev.Eta))
            {
                text += " eta=" + Fmt0(ev.Eta) + "s";
            }
            return text;
        }

        // R1 own formation attacked

        /// <summary>R1: every formation of our side (VictimMinMen; allied teams too, their main infantry's frontal clash excluded) against every significant enemy formation. Horse archers count only in melee.</summary>
        private void DetectR1(Mission mission, Formation main, float now)
        {
            foreach (Team own in mission.Teams)
            {
                if (!IsFriendlyTeam(own))
                {
                    continue;
                }
                DetectR1(mission, own, (own == _team) ? main : SideMain(own), now);
            }
        }

        private void DetectR1(Mission mission, Team own, Formation main, float now)
        {
            foreach (Formation victim in own.FormationsIncludingSpecialAndEmpty)
            {
                if (victim == null || victim.CountOfUnits < VictimMinMen || victim.AI == null)
                {
                    continue;
                }
                // User rule: a line holding under HoldLateCharge fights in place (its own behind/allyFighting cover it).
                BehaviorComponent active = victim.AI.ActiveBehavior;
                if (active is RBMBehaviorHedgehog)
                {
                    continue;
                }
                RBMBehaviorHoldChoke choke = active as RBMBehaviorHoldChoke;
                if (choke != null && choke.CurrentRole == RBMBehaviorHoldChoke.Role.Line)
                {
                    continue;
                }
                // horse archers out-ride cavalry: a victim only once caught in melee
                if (victim.QuerySystem.IsRangedCavalryFormation && Melee(victim) < R1MeleeShare)
                {
                    continue;
                }
                Vec2 vc = RBMAI.Utilities.GetFormationCenter(victim);
                if (!vc.IsValid)
                {
                    continue;
                }
                foreach (Team team in mission.Teams)
                {
                    if (team == null || !team.IsEnemyOf(_team))
                    {
                        continue;
                    }
                    foreach (Formation a in team.FormationsIncludingSpecialAndEmpty)
                    {
                        if (a == null || a.CountOfUnits <= 0 || IsRetreating(a))
                        {
                            continue;
                        }
                        Vec2 ac = RBMAI.Utilities.GetFormationCenter(a);
                        if (!ac.IsValid)
                        {
                            continue;
                        }
                        float gap = EdgeGap(victim, a);
                        if (gap > R1CavalryGap)
                        {
                            continue;
                        }
                        // Horse archers harass from range (R3); in melee with the victim they are R1 attackers.
                        bool horseArchers = a.QuerySystem.IsRangedCavalryFormation;
                        if (horseArchers && gap > R1MeleeGap)
                        {
                            continue;
                        }
                        bool cavalry = a.QuerySystem.IsCavalryFormation;
                        ReactionEvent existing = Find(ReactKind.R1, victim, a, 0);
                        if (!cavalry && !horseArchers && gap > R1FootGap && (existing == null || !existing.Confirmed))
                        {
                            continue;
                        }
                        if (!SignificantToVictim(victim, a))
                        {
                            continue;
                        }
                        // The main infantry's frontal clash is the battle itself: only flank / rear attacks count.
                        if (victim == main && !FlankOrRear(main, ac))
                        {
                            continue;
                        }
                        float closing = Closing(a, vc);
                        bool melee = gap <= R1MeleeGap && Melee(victim) >= R1MeleeShare;
                        if (cavalry && !melee)
                        {
                            float frontLateral;
                            if (FrontalCavalry(a, ac, out frontLateral) && !(gap <= FrontalCavalryReactGap && closing >= FrontalCavalryChargeSpeed))
                            {
                                if (gap <= R1CavalryGap && closing >= R1CavalryClosing)
                                {
                                    LogFrontalSkip(ReactKind.R1, victim, a, frontLateral, gap, closing, now);
                                }
                                continue;
                            }
                        }
                        bool strict;
                        bool keep;
                        if (horseArchers)
                        {
                            strict = melee;
                            keep = melee || gap <= R1MeleeGap;
                        }
                        else
                        {
                            strict = melee
                                || (!cavalry && gap <= R1FootGap && closing >= R1FootClosing)
                                || (cavalry && gap <= R1CavalryGap && closing >= R1CavalryClosing);
                            keep = melee || gap <= R1ReliefGap || closing >= R1ReliefClosing;
                        }
                        if (!strict && !keep)
                        {
                            continue;
                        }
                        Observe(ReactKind.R1, victim, 0, victim, a, strict, keep,
                            melee ? (horseArchers ? "melee horseArchers" : "melee") : (cavalry ? "cavalryClosing" : (horseArchers ? "horseArchersClose" : "closing")),
                            gap, closing, float.NaN, now);
                    }
                }
            }
        }

        /// <summary>R1 on the main infantry: the attacker's centre beyond its half width + R1MainSideMargin to the side, or behind its centre.</summary>
        private static bool FlankOrRear(Formation main, Vec2 attackerCentre)
        {
            Vec2 mc = RBMAI.Utilities.GetFormationCenter(main);
            Vec2 d = main.Direction;
            if (!mc.IsValid || d.LengthSquared < 0.01f)
            {
                return false;
            }
            d = d.Normalized();
            Vec2 rel = attackerCentre - mc;
            float along = rel.DotProduct(d);
            float lateral = MathF.Abs(rel.DotProduct(new Vec2(-d.y, d.x)));
            return lateral > main.Width * 0.5f + R1MainSideMargin || along < 0f;
        }

        private enum RiderAspect
        {
            None,
            Frontal,
            Round
        }

        /// <summary>
        /// User rule, army frame, one definition for R1 / R2 / R7. Round: beyond the main's half width + FrontalLateralMargin, R2FrontPast behind
        /// its front plane, or ahead in that lane but riding across our front past it (within FrontalLookAhead) / outward at FrontalLateralSpeed.
        /// Frontal: ahead of the front plane in that lane otherwise. None: at the front plane, or no frame.
        /// </summary>
        private RiderAspect Aspect(Formation riders, Vec2 ridersCentre, out float lateral, out float along)
        {
            lateral = float.NaN;
            along = float.NaN;
            Formation main = _frameMain;
            Vec2 forward = _frameForward;
            if (main == null || main.CountOfUnits <= 0 || !forward.IsValid || !ridersCentre.IsValid)
            {
                return RiderAspect.None;
            }
            Vec2 mc = RBMAI.Utilities.GetFormationCenter(main);
            if (!mc.IsValid)
            {
                return RiderAspect.None;
            }
            Vec2 left = new Vec2(-forward.y, forward.x);
            Vec2 rel = ridersCentre - mc;
            along = rel.DotProduct(forward);
            lateral = rel.DotProduct(left);
            float front = main.Depth * 0.5f;
            float lane = main.Width * 0.5f + FrontalLateralMargin;
            if (MathF.Abs(lateral) > lane || along < front - R2FrontPast)
            {
                return RiderAspect.Round;
            }
            if (along <= front)
            {
                return RiderAspect.None;
            }
            Vec2 velocity;
            if (!EnemyMotion.TryVelocity(riders, out velocity))
            {
                return RiderAspect.Frontal;
            }
            float comeIn = -velocity.DotProduct(forward);
            float across = velocity.DotProduct(left);
            if (comeIn > 0.5f)
            {
                float t = MathF.Min((along - front) / comeIn, FrontalLookAhead);
                return (MathF.Abs(lateral + across * t) <= lane) ? RiderAspect.Frontal : RiderAspect.Round;
            }
            return (((lateral >= 0f) ? across : -across) <= FrontalLateralSpeed) ? RiderAspect.Frontal : RiderAspect.Round;
        }

        private bool FrontalCavalry(Formation riders, Vec2 ridersCentre, out float lateral)
        {
            float along;
            return Aspect(riders, ridersCentre, out lateral, out along) == RiderAspect.Frontal;
        }

        /// <summary>Frontal riders still answered: charging in close, or the victim already in melee.</summary>
        private bool FrontalChargeIn(Formation victim, float gap, float closing)
        {
            return (gap <= FrontalCavalryReactGap && closing >= FrontalCavalryChargeSpeed) || Melee(victim) >= R1MeleeShare;
        }

        private readonly Dictionary<Formation, float> _frontalSkipLogUntil = new Dictionary<Formation, float>();

        private readonly Dictionary<Formation, float> _frontalSkipLogUntilR2 = new Dictionary<Formation, float>();

        /// <summary>"Rk skip ... why=frontalCavalry", at most once per riders and kind per FrontalSkipLogSeconds.</summary>
        private void LogFrontalSkip(ReactKind kind, Formation victim, Formation riders, float lateral, float gap, float closing, float now)
        {
            if (!Logging && !DecisionTrace.Active)
            {
                return;
            }
            Dictionary<Formation, float> until = (kind == ReactKind.R2) ? _frontalSkipLogUntilR2 : _frontalSkipLogUntil;
            float next;
            if (until.TryGetValue(riders, out next) && now < next)
            {
                return;
            }
            until[riders] = now + FrontalSkipLogSeconds;
            Log(kind, "skip", 0, victim, 0, victim, riders, null, null, float.NaN, float.NaN,
                "frontalCavalry lateral=" + Fmt0(lateral) + "m gap=" + FormationGeometry.GapText(gap) + " closing=" + Fmt1(closing) + "m/s", now);
        }

        // R2 enemy cavalry round a flank / at the archers / the rear

        /// <summary>One R2 victim: an own formation (Spot 0) or one end of the main line (Spot 1 left / 2 right) as a point.</summary>
        private struct VictimSpot
        {
            public Formation Formation;
            public int Spot;
            public Vec2 Point;
            public float Radius;
            public bool Ranged;
            public bool InFront;
        }

        private readonly List<VictimSpot> _r2Victims = new List<VictimSpot>();

        /// <summary>R2: per enemy melee cavalry formation (horse archers only within R2HorseArcherGap) the most urgent victim (lowest ETA; a confirmed event's victim is kept while it qualifies).</summary>
        private void DetectR2(Mission mission, Formation main, Vec2 forward, float now)
        {
            Vec2 mc = RBMAI.Utilities.GetFormationCenter(main);
            if (!mc.IsValid)
            {
                return;
            }
            Vec2 left = new Vec2(-forward.y, forward.x);
            float mainFront = main.Depth * 0.5f;
            float mainHalfWidth = main.Width * 0.5f;
            // an anchor that is no melee line (archers) is a victim itself, without line ends
            bool mainIsLine = FormationCohesion.IsMeleeInfantry(main);
            // Victims: own foot ranged, own formations behind the main front plane, the main line's ends; allied teams' foot ranged.
            _r2Victims.Clear();
            foreach (Team own in mission.Teams)
            {
                if (!IsFriendlyTeam(own))
                {
                    continue;
                }
                foreach (Formation f in own.FormationsIncludingSpecialAndEmpty)
                {
                    if (f == null || (f == main && mainIsLine) || f.CountOfUnits < VictimMinMen)
                    {
                        continue;
                    }
                    Vec2 c = RBMAI.Utilities.GetFormationCenter(f);
                    if (!c.IsValid)
                    {
                        continue;
                    }
                    FormationQuerySystem q = f.QuerySystem;
                    bool ranged = !q.IsCavalryFormation && !q.IsRangedCavalryFormation && RBMAI.Utilities.IsRangedForArrangement(f);
                    // horse archers out-ride cavalry: a victim only once caught in melee
                    if (q.IsRangedCavalryFormation && Melee(f) < R1MeleeShare)
                    {
                        continue;
                    }
                    float along = (c - mc).DotProduct(forward);
                    bool inFront = along > mainFront;
                    if (!ranged && (inFront || own != _team))
                    {
                        continue;
                    }
                    _r2Victims.Add(new VictimSpot { Formation = f, Spot = 0, Point = c, Radius = Radius(f), Ranged = ranged, InFront = inFront });
                }
            }
            if (mainIsLine)
            {
                _r2Victims.Add(new VictimSpot { Formation = main, Spot = 1, Point = mc + left * mainHalfWidth, Radius = 0f });
                _r2Victims.Add(new VictimSpot { Formation = main, Spot = 2, Point = mc - left * mainHalfWidth, Radius = 0f });
            }

            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(_team))
                {
                    continue;
                }
                foreach (Formation a in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (a == null || a.CountOfUnits <= 0 || IsRetreating(a))
                    {
                        continue;
                    }
                    FormationQuerySystem aq = a.QuerySystem;
                    bool horseArchers = aq.IsRangedCavalryFormation;
                    if (!horseArchers && !aq.IsCavalryFormation)
                    {
                        continue;
                    }
                    Vec2 ac = RBMAI.Utilities.GetFormationCenter(a);
                    if (!ac.IsValid)
                    {
                        continue;
                    }
                    Vec2 rel = ac - mc;
                    float along = rel.DotProduct(forward);
                    float lateral = rel.DotProduct(left);
                    float aspectLateral;
                    float aspectAlong;
                    RiderAspect aspect = Aspect(a, ac, out aspectLateral, out aspectAlong);
                    bool round = aspect == RiderAspect.Round;
                    int best = -1;
                    float bestEta = float.MaxValue;
                    bool bestStrict = false;
                    bool bestKeep = false;
                    bool bestKept = false;
                    float bestGap = 0f;
                    float bestClosing = 0f;
                    for (int i = 0; i < _r2Victims.Count; i++)
                    {
                        VictimSpot v = _r2Victims[i];
                        float gap = SpotGap(v.Formation, v.Spot, v.Point, a);
                        if (gap > R2MaxGap || (horseArchers && gap > R2HorseArcherGap))
                        {
                            continue;
                        }
                        // Archers standing in front of the line count head-on too, unless own infantry screens them.
                        bool frontal = v.Ranged && v.InFront && !Screened(v.Formation, ac, v.Point);
                        if (!round && !frontal)
                        {
                            continue;
                        }
                        if (!SignificantToVictim(v.Formation, a))
                        {
                            continue;
                        }
                        float closing = Closing(a, v.Point);
                        float eta = (closing > 0.1f) ? gap / closing : float.MaxValue;
                        bool strict = closing >= R2Closing && eta <= R2MaxEta;
                        bool keep = closing >= R2ReliefClosing && eta <= R2RelieveEta;
                        ReactionEvent existing = Find(ReactKind.R2, v.Formation, a, v.Spot);
                        bool confirmed = existing != null && existing.Confirmed;
                        if (!strict && !(confirmed && keep))
                        {
                            continue;
                        }
                        // user rule: archers in front are answered against frontal riders only when they charge in
                        if (!round && aspect == RiderAspect.Frontal && !FrontalChargeIn(v.Formation, gap, closing))
                        {
                            if (strict)
                            {
                                LogFrontalSkip(ReactKind.R2, v.Formation, a, lateral, gap, closing, now);
                            }
                            continue;
                        }
                        // A confirmed event's victim stays the pick while it still qualifies (no victim flip-flop).
                        bool better = (confirmed && !bestKept) || ((confirmed == bestKept) && eta < bestEta);
                        if (best < 0 || better)
                        {
                            best = i;
                            bestEta = eta;
                            bestStrict = strict;
                            bestKeep = keep;
                            bestKept = confirmed;
                            bestGap = gap;
                            bestClosing = closing;
                        }
                    }
                    // Riders already fighting are not riding round, but an event already watched on them stays while they fight near its victim (KeepR2InMelee).
                    if (best < 0)
                    {
                        KeepR2InMelee(a, now);
                        continue;
                    }
                    if (Melee(a) >= R2AttackerMeleeMax)
                    {
                        KeepR2InMelee(a, now);
                        continue;
                    }
                    VictimSpot pick = _r2Victims[best];
                    Observe(ReactKind.R2, pick.Formation, pick.Spot, pick.Formation, a, bestStrict, bestKeep,
                        (round ? "round" : "frontal") + (horseArchers ? " horseArchers" : "") + " lateral=" + Fmt0(lateral) + "m along=" + Fmt0(along) + "m",
                        bestGap, bestClosing, (bestEta >= float.MaxValue) ? float.NaN : bestEta, now);
                }
            }
        }

        /// <summary>Edge gap from the riders to a victim spot: the victim's footprint (Spot 0) or a line end point.</summary>
        private static float SpotGap(Formation victim, int spot, Vec2 point, Formation riders)
        {
            return (spot == 0) ? EdgeGap(victim, riders) : GapToPoint(riders, point);
        }

        private const float R2MeleeKeepGap = 60f; // edge gap to the victim spot

        /// <summary>R2, riders no longer riding round: every confirmed R2 event on them is kept (no new event) while they are in melee within R2MeleeKeepGap of its victim spot (the R2 -> R1 hand-over).</summary>
        private void KeepR2InMelee(Formation riders, float now)
        {
            bool any = false;
            for (int i = 0; i < _events.Count; i++)
            {
                if (_events[i].Kind == ReactKind.R2 && _events[i].Attacker == riders && _events[i].Confirmed)
                {
                    any = true;
                    break;
                }
            }
            if (!any)
            {
                return;
            }
            float melee = Melee(riders);
            if (melee < R2AttackerMeleeMax)
            {
                return;
            }
            Vec2 ac = RBMAI.Utilities.GetFormationCenter(riders);
            if (!ac.IsValid)
            {
                return;
            }
            int count = _events.Count;
            for (int i = 0; i < count; i++)
            {
                ReactionEvent ev = _events[i];
                if (ev.Kind != ReactKind.R2 || ev.Attacker != riders || !ev.Confirmed || ev.Seen)
                {
                    continue;
                }
                Vec2 point = VictimAnchorFor(ev.Victim, ev.Spot);
                if (!point.IsValid)
                {
                    continue;
                }
                float gap = SpotGap(ev.Victim, ev.Spot, point, riders);
                if (gap <= R2MeleeKeepGap)
                {
                    Observe(ReactKind.R2, ev.Victim, ev.Spot, Reactor(ev), riders, false, true, "ridersInMelee melee=" + Fmt2(melee), gap, 0f, float.NaN, now);
                }
            }
        }

        // R7 shadow: enemy riders parked round our flank / rear

        private const float R7MaxSpeed = 2f; // parked under this sampled speed (m/s) to start, R7KeepSpeed to keep

        private const float R7KeepSpeed = 3f;

        private const float R7Range = 150f; // edge gap to a victim spot to start, + R7KeepSlack to keep

        private const float R7KeepSlack = 50f;

        private const int R7ConfirmChecks = 12;

        private const float R7RangedFactor = 0.7f; // a foot ranged victim counts this much nearer (likely prey)

        /// <summary>R7 shadow, after DetectR2 (its victim spots): per enemy mounted formation round our main line, not in melee, not already an R2 event, parked (under R7MaxSpeed) within R7Range of a vulnerable victim spot it is significant to. Keyed on the riders alone. Victims: R2's foot spots only, never line ends or mounted formations.</summary>
        private void DetectR7(Mission mission, Formation main, Vec2 forward, float now)
        {
            Vec2 mc = RBMAI.Utilities.GetFormationCenter(main);
            if (!mc.IsValid || _r2Victims.Count == 0)
            {
                return;
            }
            Vec2 left = new Vec2(-forward.y, forward.x);
            float mainFront = main.Depth * 0.5f;
            float mainHalfWidth = main.Width * 0.5f;
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(_team))
                {
                    continue;
                }
                foreach (Formation a in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (a == null || a.CountOfUnits <= 0 || IsRetreating(a))
                    {
                        continue;
                    }
                    FormationQuerySystem aq = a.QuerySystem;
                    if (!aq.IsCavalryFormation && !aq.IsRangedCavalryFormation)
                    {
                        continue;
                    }
                    Vec2 ac = RBMAI.Utilities.GetFormationCenter(a);
                    float speed = SampledSpeed(a);
                    if (!ac.IsValid || float.IsNaN(speed) || speed >= R7KeepSpeed)
                    {
                        continue;
                    }
                    Vec2 rel = ac - mc;
                    float along = rel.DotProduct(forward);
                    float lateral = rel.DotProduct(left);
                    float aspectLateral;
                    float aspectAlong;
                    if (Aspect(a, ac, out aspectLateral, out aspectAlong) != RiderAspect.Round)
                    {
                        continue;
                    }
                    // Riding in on someone is R2's (R7 hands its task over when it closes).
                    bool r2 = false;
                    for (int i = 0; i < _events.Count; i++)
                    {
                        if (_events[i].Kind == ReactKind.R2 && _events[i].Attacker == a && _events[i].Confirmed)
                        {
                            r2 = true;
                            break;
                        }
                    }
                    if (r2)
                    {
                        continue;
                    }
                    int best = -1;
                    float bestKey = float.MaxValue;
                    float bestGap = 0f;
                    for (int i = 0; i < _r2Victims.Count; i++)
                    {
                        VictimSpot v = _r2Victims[i];
                        // User rule: only a VULNERABLE victim (foot ranged or foot behind the line), never a line end or mounted formation.
                        if (v.Spot != 0 || v.Formation == null || v.Formation.QuerySystem.IsCavalryFormation || v.Formation.QuerySystem.IsRangedCavalryFormation)
                        {
                            continue;
                        }
                        float gap = SpotGap(v.Formation, v.Spot, v.Point, a);
                        if (gap > R7Range + R7KeepSlack || !SignificantToVictim(v.Formation, a))
                        {
                            continue;
                        }
                        float key = MathF.Max(gap, 1f) * (v.Ranged ? R7RangedFactor : 1f);
                        if (key < bestKey)
                        {
                            best = i;
                            bestKey = key;
                            bestGap = gap;
                        }
                    }
                    if (best < 0 || Melee(a) >= R2AttackerMeleeMax)
                    {
                        continue;
                    }
                    VictimSpot pick = _r2Victims[best];
                    bool strict = speed < R7MaxSpeed && bestGap <= R7Range;
                    Observe(ReactKind.R7, pick.Formation, pick.Spot, pick.Formation, a, strict, true,
                        "shadow" + (aq.IsRangedCavalryFormation ? " horseArchers" : "") + " speed=" + Fmt1(speed) + "m/s lateral=" + Fmt0(lateral) + "m along=" + Fmt0(along) + "m",
                        bestGap, Closing(a, pick.Point), float.NaN, now);
                }
            }
        }

        /// <summary>Own infantry (not ranged, at least FreeMinMen) within R2ScreenRange m of the riders' line to <paramref name="point"/>: they are screened (ArcherCover's test).</summary>
        private bool Screened(Formation victim, Vec2 riders, Vec2 point)
        {
            foreach (Formation f in _team.FormationsIncludingSpecialAndEmpty)
            {
                if (f == null || f == victim || f.CountOfUnits < FreeMinMen || !f.QuerySystem.IsInfantryFormation || RBMAI.Utilities.IsRangedForArrangement(f))
                {
                    continue;
                }
                Vec2 c = RBMAI.Utilities.GetFormationCenter(f);
                if (!c.IsValid)
                {
                    continue;
                }
                Vec2 q = ClosestOnSegment(c, riders, point);
                if (GapToSegment(f, riders, point) <= R2ScreenRange && q.DistanceSquared(riders) > 1f && q.DistanceSquared(point) > 1f)
                {
                    return true;
                }
            }
            return false;
        }

        // R4 the opening the player leaves

        /// <summary>R4: an enemy formation significant to our main infantry, R4EnemyMelee in melee, with an own formation within R4OwnGap at R4OwnMelee; confirmed after R4LockChecks, kept while R4EnemyMeleeKeep in melee. Openness is judged at the assignment (OpenSide).</summary>
        private void DetectR4(Mission mission, Formation main, float now)
        {
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(_team))
                {
                    continue;
                }
                foreach (Formation e in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (e == null || e.CountOfUnits <= 0 || IsRetreating(e) || !ThreatSignificance.IsSignificant(main, e))
                    {
                        continue;
                    }
                    Vec2 ec = RBMAI.Utilities.GetFormationCenter(e);
                    if (!ec.IsValid)
                    {
                        continue;
                    }
                    Footprint eShape;
                    if (!FormationGeometry.TryGet(e, FootprintMode.ScatterAware, out eShape))
                    {
                        continue;
                    }
                    // The nearest formation of our side (allied too) within R4OwnGap that is in melee (cheap gap test first).
                    Formation locked = null;
                    float lockedGap = float.MaxValue;
                    foreach (Team own in mission.Teams)
                    {
                        if (!IsFriendlyTeam(own))
                        {
                            continue;
                        }
                        foreach (Formation o in own.FormationsIncludingSpecialAndEmpty)
                        {
                            if (o == null || o.CountOfUnits < VictimMinMen)
                            {
                                continue;
                            }
                            Footprint oShape;
                            if (!FormationGeometry.TryGet(o, FootprintMode.ScatterAware, out oShape))
                            {
                                continue;
                            }
                            float gap = FormationGeometry.Gap(eShape, oShape);
                            if (gap <= R4OwnGap && gap < lockedGap && Melee(o) >= R4OwnMelee)
                            {
                                locked = o;
                                lockedGap = gap;
                            }
                        }
                    }
                    ReactionEvent existing = Find(ReactKind.R4, null, e, 0);
                    if (locked == null && (existing == null || !existing.Confirmed))
                    {
                        continue;
                    }
                    float enemyMelee = Melee(e);
                    bool strict = locked != null && enemyMelee >= R4EnemyMelee;
                    bool keep = enemyMelee >= R4EnemyMeleeKeep;
                    if (!strict && !keep)
                    {
                        continue;
                    }
                    Formation victim = locked ?? existing.Victim;
                    Observe(ReactKind.R4, victim, 0, main, e, strict, keep, "locked melee=" + Fmt2(enemyMelee), (locked != null) ? lockedGap : float.NaN, 0f, float.NaN, now);
                }
            }
        }

        // expiry

        /// <summary>Drops unconfirmed events not seen this check; closes confirmed ones with a CLEAR line.</summary>
        private void ExpireEvents(float now)
        {
            for (int i = _events.Count - 1; i >= 0; i--)
            {
                ReactionEvent ev = _events[i];
                if (!ev.Confirmed)
                {
                    if (!ev.Seen)
                    {
                        _events.RemoveAt(i);
                    }
                    continue;
                }
                string why = null;
                if (ev.Attacker == null || ev.Attacker.CountOfUnits <= 0)
                {
                    why = "attackerGone";
                }
                else if (IsRetreating(ev.Attacker))
                {
                    why = "attackerRetreat";
                }
                else if (ev.Kind == ReactKind.R3 && !IsHuntTarget(ev.Attacker))
                {
                    why = "attackerNotRanged";
                }
                else if (ev.Victim == null || ev.Victim.CountOfUnits <= 0)
                {
                    why = "victimGone";
                }
                else
                {
                    float quiet = now - ev.LastTrue;
                    float limit = (ev.Kind == ReactKind.R2) ? R2RelieveSeconds : CloseAfterSeconds;
                    if (quiet >= limit)
                    {
                        why = "relieved quiet=" + Fmt0(quiet) + "s";
                    }
                }
                if (why == null)
                {
                    continue;
                }
                _events.RemoveAt(i);
                CloseEvent(ev, why, now);
            }
        }

        /// <summary>A closed event: its task, unless already fighting, moves to another live event on the same attacker ("handoff"), or an Intercept / Deter screen whose riders stay within R2MaxGap keeps its screen ("screenHeld"); else ends "relieved". A CLEAR line.</summary>
        private void CloseEvent(ReactionEvent ev, string why, float now)
        {
            ReactTask task = ev.Task;
            string kept = null;
            if (task != null)
            {
                RBMBehaviorReact react = BehaviorOf(task);
                if (react != null && react.Task == task && react.CurrentPhase != RBMBehaviorReact.Phase.Fight)
                {
                    ReactionEvent next = HandoffTarget(ev);
                    if (next != null)
                    {
                        next.Task = task;
                        next.Tasks++;
                        task.Event = next;
                        task.Victim = next.Victim;
                        task.VictimSpot = next.Spot;
                        task.Reactor = Reactor(next);
                        kept = "handoff=" + next.Kind + ":" + next.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        Covered(next, react.Formation, task.Mode.ToString(), "handoff task id=" + task.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            + " from " + ev.Kind + " id=" + ev.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), now);
                    }
                    else if (ScreenHeld(task, why))
                    {
                        kept = "screenHeld";
                    }
                    else
                    {
                        react.End("relieved (" + why + ")", now);
                    }
                }
            }
            LogEventLine(ev, "clear", null, null, float.NaN, float.NaN, why + " age=" + Fmt0(now - ev.FirstSeen) + "s tasks=" + ev.Tasks.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + (ev.Answer != null ? " answer=" + ev.Answer : "") + (kept != null ? " task=" + kept : ""), now);
        }

        /// <summary>A live confirmed event on the same attacker with no task and no line answer (the closed one is already out), or null.</summary>
        private ReactionEvent HandoffTarget(ReactionEvent closed)
        {
            if (closed.Attacker == null || closed.Attacker.CountOfUnits <= 0)
            {
                return null;
            }
            ReactionEvent best = null;
            for (int i = 0; i < _events.Count; i++)
            {
                ReactionEvent ev = _events[i];
                if (ev == closed || !ev.Confirmed || ev.Task != null || ev.Answer != null || ev.Attacker != closed.Attacker
                    || ev.Victim == null || ev.Victim.CountOfUnits <= 0)
                {
                    continue;
                }
                if (best == null || Rank(ev.Kind) < Rank(best.Kind))
                {
                    best = ev;
                }
            }
            return best;
        }

        /// <summary>An Intercept / Deter screen of an event closed "relieved" whose riders are still within R2MaxGap of the victim it screens.</summary>
        private bool ScreenHeld(ReactTask task, string why)
        {
            if ((task.Mode != ReactMode.Intercept && task.Mode != ReactMode.Deter) || why == null || !why.StartsWith("relieved")
                || task.Target == null || task.Target.CountOfUnits <= 0)
            {
                return false;
            }
            Vec2 anchor = VictimAnchorFor(task.Victim, task.VictimSpot);
            return anchor.IsValid && GapToPoint(task.Target, anchor) <= R2MaxGap;
        }

        /// <summary>The live behavior carrying <paramref name="task"/>, or null.</summary>
        private RBMBehaviorReact BehaviorOf(ReactTask task)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i] != null && _active[i].Task == task)
                {
                    return _active[i];
                }
            }
            return null;
        }
    }
}
