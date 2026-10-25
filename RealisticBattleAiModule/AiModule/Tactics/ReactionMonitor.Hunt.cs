using System.Collections.Generic;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// ReactionMonitor's R3, hunt the enemy archers (AI thread).
    /// DETECT, per enemy ranged formation A (foot ranged or horse archers: IsHuntTarget), keyed on A alone: "hits" = an own
    /// formation A is significant to took landed missile hits this check (UnderRangedAttackRatio >= R3HitRatio) with A within
    /// R3Range; "unanswered" = A stood within R3Range of our main infantry for R3SustainSeconds while our ranged power is
    /// under R3NoAnswerShare x the enemy's. Confirmed after R3ConfirmChecks, kept while A stays within R3Range + R3KeepSlack
    /// of a hit formation; priority Key favours archers actually shooting.
    /// PLAN, per responder (bait-proof): never infantry against horse archers; cavalry hunts horse archers only while nearly
    /// still (R3HaMaxSpeed); riding horse archers get a DETER instead (a hunt turns into one on "haRunning"); no enemy melee
    /// cavalry near (trap), no enemy melee foot near A ("screened") or on the way. Cavalry rides to A's nearest open
    /// left/right/rear stage point, else straight at A ("frontal") when clear; infantry only within R3InfantryMaxGap, charging at once.
    /// Horse archers are never hunters. LIMITS: MaxHuntTasks per team, R3TeamCooldown, R3TargetCooldown, "covered" while an own
    /// task strikes A; the behavior ends a hunt "screened" / "notRanged" / by its chase, leash and timeout rules.
    /// </summary>
    internal sealed partial class ReactionMonitor
    {

        private const float R3HitRatio = HoldLateCharge.OutshotHitRatio;

        private const float R3HitGap = HoldLateCharge.OutshotHitGap; // s a formation counts as shot at after its last landed hits

        private const float R3Range = 200f; // plausible shooting range, edge gap

        private const float R3KeepSlack = 50f;

        private const float R3FacingCos = 0.5f; // A faces a formation within ~60 degrees of its direction

        private const float R3NotFacingFactor = 1.5f;

        private const float R3NotShootingFactor = 1.5f;

        private const float R3UnansweredFactor = 1.5f;

        private const float R3SustainSeconds = 20f;

        private const float R3NoAnswerShare = 0.6f; // "no ranged answer": our ranged power under this share of the enemy's

        private const int R3ConfirmChecks = 3;

        private const float R3ClearTarget = 60f; // screened: significant enemy melee foot this close (edge gap) to the archers

        private const float R3ClearPath = 40f; // the way must pass significant enemy melee foot by this (edge) ...

        private const float R3MainFrontMargin = 50f; // ... and not in front of the largest of them within its half width + this

        private const float R3InfantryMaxGap = 80f;

        private const float R3MaxTtaCavalry = 40f;

        private const float R3BeyondMargin = 20f; // goal stays this far inside the cavalry leash past the enemy main line

        private const float R3HaMaxSpeed = 3f; // horse archers are hunted only under this sampled speed

        private const int R3HaRunChecks = 3;

        private const float R3FrontalFactor = 1.3f;

        private const float R3DeterFactor = 1.2f; // a Deter screen scores this much worse than a hunt

        private const float R3DeterHitRatio = 0.1f; // Deter (moving HA) only while the victim takes this share of missile hits ...

        private const float R3DeterRange = 120f; // ... with the HA this close (edge gap)

        private const float ClassCavalryVsRanged = 0.6f;

        private const float ClassInfantryVsRanged = 1.5f;

        private const int MaxHuntTasks = 1;

        private const float R3TeamCooldown = 20f;

        private const float R3TargetCooldown = 60f;


        private readonly Dictionary<Formation, float> _lastRangedHit = new Dictionary<Formation, float>(); // UnderRangedAttackRatio at least R3HitRatio

        private readonly Dictionary<Formation, float> _r3InRangeSince = new Dictionary<Formation, float>(); // enemy ranged within R3Range of our main infantry

        private readonly Dictionary<Formation, float> _huntTargetCooldown = new Dictionary<Formation, float>();

        private readonly List<Formation> _r3Victims = new List<Formation>();

        private float _huntTeamCooldownUntil = float.NegativeInfinity;

        /// <summary>A formation R3 hunts: foot ranged (not cavalry, Utilities.IsRangedForArrangement) or horse archers.</summary>
        internal static bool IsHuntTarget(Formation formation)
        {
            if (formation == null || formation.CountOfUnits <= 0)
            {
                return false;
            }
            FormationQuerySystem q = formation.QuerySystem;
            return q.IsRangedCavalryFormation || (!q.IsCavalryFormation && RBMAI.Utilities.IsRangedForArrangement(formation));
        }

        private static bool IsMeleeFoot(Formation formation)
        {
            FormationQuerySystem q = formation.QuerySystem;
            return !q.IsCavalryFormation && !q.IsRangedCavalryFormation && !RBMAI.Utilities.IsRangedForArrangement(formation);
        }

        private void DetectR3(Mission mission, Formation main, float now)
        {
            // Every team of our side: an allied AI team hunts what shoots the player's formations.
            _r3Victims.Clear();
            foreach (Team own in mission.Teams)
            {
                if (!IsFriendlyTeam(own))
                {
                    continue;
                }
                foreach (Formation f in own.FormationsIncludingSpecialAndEmpty)
                {
                    if (f == null || f.CountOfUnits < VictimMinMen)
                    {
                        continue;
                    }
                    _r3Victims.Add(f);
                    if (f.QuerySystem.UnderRangedAttackRatio >= R3HitRatio)
                    {
                        _lastRangedHit[f] = now;
                    }
                }
            }
            Vec2 mc = (main != null) ? RBMAI.Utilities.GetFormationCenter(main) : Vec2.Invalid;
            bool noAnswer = false;
            float ownRanged = 0f;
            float enemyRanged = 0f;
            if (mc.IsValid)
            {
                SideRangedPower(mission, out ownRanged, out enemyRanged);
                noAnswer = enemyRanged > 0f && ownRanged < R3NoAnswerShare * enemyRanged;
            }
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(_team))
                {
                    continue;
                }
                foreach (Formation a in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (a == null || a.CountOfUnits <= 0)
                    {
                        continue;
                    }
                    if (!IsHuntTarget(a) || IsRetreating(a))
                    {
                        _r3InRangeSince.Remove(a);
                        continue;
                    }
                    Vec2 ac = RBMAI.Utilities.GetFormationCenter(a);
                    if (!ac.IsValid)
                    {
                        continue;
                    }
                    Vec2 facing = a.Direction;
                    bool hasFacing = facing.LengthSquared >= 0.01f;
                    if (hasFacing)
                    {
                        facing = facing.Normalized();
                    }
                    // Unanswered: standing in range of our main infantry with no ranged answer on our side.
                    float mainGap = float.MaxValue;
                    bool mainSignificant = false;
                    if (mc.IsValid)
                    {
                        mainGap = EdgeGap(a, main);
                        mainSignificant = SignificantToVictim(main, a);
                    }
                    bool mainInRange = mainSignificant && mainGap <= R3Range;
                    float since;
                    if (mainInRange)
                    {
                        if (!_r3InRangeSince.TryGetValue(a, out since))
                        {
                            since = now;
                            _r3InRangeSince[a] = now;
                        }
                    }
                    else
                    {
                        _r3InRangeSince.Remove(a);
                        since = now;
                    }
                    bool unanswered = mainInRange && noAnswer && now - since >= R3SustainSeconds;

                    // The own formation it most likely shoots at: hit this check first, then the nearest (facing it).
                    Formation best = null;
                    float bestKey = float.MaxValue;
                    float bestGap = 0f;
                    bool bestNow = false;
                    bool bestFacing = false;
                    for (int vi = 0; vi < _r3Victims.Count; vi++)
                    {
                        Formation v = _r3Victims[vi];
                        float last;
                        if (!_lastRangedHit.TryGetValue(v, out last) || now - last > R3HitGap)
                        {
                            continue;
                        }
                        Vec2 vc = RBMAI.Utilities.GetFormationCenter(v);
                        if (!vc.IsValid)
                        {
                            continue;
                        }
                        float gap = EdgeGap(v, a);
                        if (gap > R3Range + R3KeepSlack || !SignificantToVictim(v, a))
                        {
                            continue;
                        }
                        bool hitNow = last >= now && gap <= R3Range;
                        Vec2 to = vc - ac;
                        bool faces = hasFacing && to.LengthSquared > 1f && facing.DotProduct(to.Normalized()) >= R3FacingCos;
                        float key = MathF.Max(gap, 1f) * (faces ? 1f : R3NotFacingFactor);
                        if (best == null || (hitNow && !bestNow) || (hitNow == bestNow && key < bestKey))
                        {
                            best = v;
                            bestKey = key;
                            bestGap = gap;
                            bestNow = hitNow;
                            bestFacing = faces;
                        }
                    }
                    bool strict;
                    Formation victim;
                    float eventGap;
                    float eventKey;
                    string trigger;
                    if (best != null && bestNow)
                    {
                        strict = true;
                        victim = best;
                        eventGap = bestGap;
                        eventKey = bestKey;
                        trigger = "hits=" + Fmt2(best.QuerySystem.UnderRangedAttackRatio) + (bestFacing ? " facing" : "");
                    }
                    else if (unanswered)
                    {
                        strict = true;
                        victim = main;
                        eventGap = mainGap;
                        eventKey = MathF.Max(mainGap, 1f) * R3UnansweredFactor;
                        trigger = "unanswered " + Fmt0(now - since) + "s ranged=" + Fmt0(ownRanged) + "/" + Fmt0(enemyRanged);
                    }
                    else if (best != null)
                    {
                        // Keep only: hit within R3HitGap s, not this check.
                        strict = false;
                        victim = best;
                        eventGap = bestGap;
                        eventKey = bestKey;
                        trigger = "hitsRecent";
                    }
                    else if (mainSignificant && noAnswer && mainGap <= R3Range + R3KeepSlack)
                    {
                        strict = false;
                        victim = main;
                        eventGap = mainGap;
                        eventKey = MathF.Max(mainGap, 1f) * R3UnansweredFactor;
                        trigger = "inRange";
                    }
                    else
                    {
                        continue;
                    }
                    bool horseArchers = a.QuerySystem.IsRangedCavalryFormation;
                    if (a.QuerySystem.MakingRangedAttackRatio <= 0f)
                    {
                        eventKey *= R3NotShootingFactor;
                        trigger += " notShooting";
                    }
                    ReactionEvent ev = Observe(ReactKind.R3, victim, 0, victim, a, strict, true, trigger + (horseArchers ? " horseArchers" : ""),
                        eventGap, 0f, float.NaN, now, OldGap(victim, a));
                    if (ev != null)
                    {
                        ev.Key = eventKey;
                    }
                }
            }
        }

        /// <summary>The ranged power (foot ranged + horse archers, cached FormationPower) of our side and of the enemy's.</summary>
        private void SideRangedPower(Mission mission, out float own, out float enemy)
        {
            own = 0f;
            enemy = 0f;
            foreach (Team team in mission.Teams)
            {
                if (team == null)
                {
                    continue;
                }
                bool enemySide = team.IsEnemyOf(_team);
                if (!enemySide && team.Side != _team.Side)
                {
                    continue;
                }
                foreach (Formation f in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (f == null || f.CountOfUnits <= 0 || !RBMAI.Utilities.IsRangedForArrangement(f))
                    {
                        continue;
                    }
                    if (enemySide)
                    {
                        enemy += f.QuerySystem.FormationPower;
                    }
                    else
                    {
                        own += f.QuerySystem.FormationPower;
                    }
                }
            }
        }

        /// <summary>The sampled speed (m/s) of an enemy formation; NaN without a velocity.</summary>
        private float SampledSpeed(Formation formation)
        {
            Sample s;
            if (formation == null || !_samples.TryGetValue(formation, out s) || !s.HasVelocity)
            {
                return float.NaN;
            }
            return s.Velocity.Length;
        }

        /// <summary>User rule: a Deter against riding horse archers only while they really hurt the victim (UnderRangedAttackRatio >= R3DeterHitRatio, within R3DeterRange); else "notThreat(hits=0.04 gap=150m)".</summary>
        private static string DeterNotThreat(Formation victim, Formation riders)
        {
            if (victim == null || riders == null)
            {
                return "notThreat(noVictim)";
            }
            float hits = victim.QuerySystem.UnderRangedAttackRatio;
            float gap = EdgeGap(victim, riders);
            if (hits < R3DeterHitRatio || gap > R3DeterRange)
            {
                return "notThreat(hits=" + Fmt2(hits) + " gap=" + FormationGeometry.Shadow(gap, OldGap(victim, riders)) + ")";
            }
            return null;
        }

        /// <summary>A hunt on horse archers before contact: they rode at R3HaMaxSpeed or more for R3HaRunChecks checks ("haRunning"); else null.</summary>
        private string HorseArchersRunning(ReactTask task)
        {
            if (task == null || task.Mode != ReactMode.Hunt || task.Target == null || !task.Target.QuerySystem.IsRangedCavalryFormation)
            {
                return null;
            }
            float speed = SampledSpeed(task.Target);
            if (float.IsNaN(speed) || speed < R3HaMaxSpeed)
            {
                task.HaRunChecks = 0;
                return null;
            }
            task.HaRunChecks++;
            return (task.HaRunChecks >= R3HaRunChecks) ? "haRunning speed=" + Fmt1(speed) + "m/s" : null;
        }

        /// <summary>R3: the free formation's hunt (stage side or frontal / direct, goal, time, class factor), or why it cannot hunt the target.</summary>
        private string PlanHunt(Mission mission, Formation r, Formation target, Formation victim, Vec2 rc, bool cavalry, float speed, out StageSide side,
            out bool strikeAtOnce, out Vec2 goal, out float tta, out float classFactor, out string note, out bool deter)
        {
            side = StageSide.None;
            strikeAtOnce = false;
            goal = Vec2.Invalid;
            tta = 0f;
            classFactor = 1f;
            note = null;
            deter = false;
            Vec2 ac = RBMAI.Utilities.GetFormationCenter(target);
            if (!ac.IsValid)
            {
                return "noPosition";
            }
            bool horseArchers = target.QuerySystem.IsRangedCavalryFormation;
            if (horseArchers)
            {
                if (!cavalry)
                {
                    return "infVsHorseArchers";
                }
                float haSpeed = SampledSpeed(target);
                if (float.IsNaN(haSpeed))
                {
                    return "haSpeedUnknown";
                }
                if (haSpeed >= R3HaMaxSpeed)
                {
                    // Moving horse archers are never chased (exploit): a Deter screen by their victim, only while they really hurt it.
                    string notThreat = DeterNotThreat(victim, target);
                    if (notThreat != null)
                    {
                        return notThreat;
                    }
                    string trapNear = CavalryTrap(mission, r, target, rc, 0f, true);
                    if (trapNear != null)
                    {
                        return trapNear;
                    }
                    goal = DeterGoal(victim, 0, target, r);
                    if (!goal.IsValid)
                    {
                        return "noAnchor";
                    }
                    tta = GapToPoint(r, goal) / speed;
                    if (tta > R3MaxTtaCavalry)
                    {
                        return "tta(" + Fmt0(tta) + "s)";
                    }
                    deter = true;
                    classFactor = R3DeterFactor;
                    note = "deter haMoving=" + Fmt1(haSpeed) + "m/s";
                    return null;
                }
            }
            // A trap: significant enemy melee cavalry near the responder or the archers.
            string trap = CavalryTrap(mission, r, target, rc, 0f, true) ?? CavalryTrap(mission, r, target, ac, 0f, false, target);
            if (trap != null)
            {
                return trap;
            }
            float screenGap;
            Formation screen = ScreeningFoot(mission, _team, r, target, R3ClearTarget, out screenGap);
            if (screen != null)
            {
                return "screened " + TerrainCavalry.FormationName(screen) + "(gap=" + FormationGeometry.Shadow(screenGap, OldGap(screen, target)) + ")";
            }
            if (!cavalry)
            {
                float gap = EdgeGap(r, target);
                if (gap > R3InfantryMaxGap)
                {
                    return "far(gap=" + FormationGeometry.Shadow(gap, OldGap(r, target)) + ")";
                }
                string blocked = HuntPathBlocked(mission, r, target, rc, ac);
                if (blocked != null)
                {
                    return blocked;
                }
                goal = ac;
                tta = gap / speed;
                strikeAtOnce = true;
                classFactor = ClassInfantryVsRanged;
                note = "direct";
                return null;
            }
            string closed;
            side = HuntSide(mission, r, target, rc, out goal, out closed);
            float baseFactor = horseArchers ? 1f : ClassCavalryVsRanged;
            if (side == StageSide.None)
            {
                // Every side shut: straight at them only when that way is clear (their own front is no reason to stay away).
                string direct = HuntPathBlocked(mission, r, target, rc, ac);
                if (direct != null)
                {
                    return closed ?? direct;
                }
                goal = ac;
                tta = EdgeGap(r, target) / speed;
                if (tta > R3MaxTtaCavalry)
                {
                    return "tta(" + Fmt0(tta) + "s)";
                }
                strikeAtOnce = true;
                classFactor = baseFactor * R3FrontalFactor;
                note = "frontal (" + (closed ?? "-") + ")";
                return null;
            }
            tta = GapToPoint(r, goal) / speed;
            if (tta > R3MaxTtaCavalry)
            {
                return "tta(" + Fmt0(tta) + "s)";
            }
            classFactor = baseFactor;
            note = side.ToString().ToLowerInvariant();
            return null;
        }

        /// <summary>R3, cavalry: the nearest of the archers' left / right / rear stage points that is walkable, not bad ground, not through their own front and over a clear way (HuntPathBlocked); None with the sides' reasons in why.</summary>
        private StageSide HuntSide(Mission mission, Formation responder, Formation target, Vec2 rc, out Vec2 goal, out string why)
        {
            goal = Vec2.Invalid;
            why = null;
            BattleTerrain terrain = BattleTerrain.Current;
            StageSide best = StageSide.None;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < Sides.Length; i++)
            {
                StageSide side = Sides[i];
                Vec2 p = SidePoint(target, side, RBMBehaviorReact.HuntFlankOut, RBMBehaviorReact.HuntRearOut);
                string closed;
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
                    closed = HuntPathBlocked(mission, responder, target, rc, p);
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
                    goal = p;
                }
            }
            if (best != StageSide.None)
            {
                why = null;
            }
            return best;
        }

        /// <summary>R3: the way passes significant enemy melee foot within R3ClearPath ("enemyOnPath") or the front of the largest of them ("enemyFront"); else null. Archers and riders on the way do not count.</summary>
        private string HuntPathBlocked(Mission mission, Formation responder, Formation target, Vec2 rc, Vec2 p)
        {
            Formation largest = null;
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(_team))
                {
                    continue;
                }
                foreach (Formation f in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (f == null || f == target || f.CountOfUnits <= 0 || IsRetreating(f) || !IsMeleeFoot(f))
                    {
                        continue;
                    }
                    Vec2 fc = RBMAI.Utilities.GetFormationCenter(f);
                    if (!fc.IsValid || !ThreatSignificance.IsSignificant(responder, f))
                    {
                        continue;
                    }
                    if (GapToSegment(f, rc, p) <= R3ClearPath)
                    {
                        return "enemyOnPath " + TerrainCavalry.FormationName(f);
                    }
                    if (largest == null || f.CountOfUnits > largest.CountOfUnits)
                    {
                        largest = f;
                    }
                }
            }
            if (largest != null && CrossesFront(rc, p, largest, R3MainFrontMargin))
            {
                return "enemyFront " + TerrainCavalry.FormationName(largest);
            }
            return null;
        }

        /// <summary>AI thread (the plan and RBMBehaviorReact's "screened" end): the nearest enemy melee foot formation, significant to the responder and not retreating, within maxGap (edge gap) of the target; null without one.</summary>
        internal static Formation ScreeningFoot(Mission mission, Team team, Formation responder, Formation target, float maxGap, out float gap)
        {
            gap = float.MaxValue;
            if (mission == null || team == null || target == null)
            {
                return null;
            }
            Footprint targetShape;
            if (!FormationGeometry.TryGet(target, FootprintMode.ScatterAware, out targetShape))
            {
                return null;
            }
            Formation nearest = null;
            foreach (Team other in mission.Teams)
            {
                if (other == null || !other.IsEnemyOf(team))
                {
                    continue;
                }
                foreach (Formation f in other.FormationsIncludingSpecialAndEmpty)
                {
                    if (f == null || f == target || f.CountOfUnits <= 0 || IsRetreating(f) || !IsMeleeFoot(f))
                    {
                        continue;
                    }
                    Footprint shape;
                    if (!FormationGeometry.TryGet(f, FootprintMode.ScatterAware, out shape))
                    {
                        continue;
                    }
                    float g = FormationGeometry.Gap(targetShape, shape);
                    if (g <= maxGap && g < gap && ThreatSignificance.IsSignificant(responder, f))
                    {
                        gap = g;
                        nearest = f;
                    }
                }
            }
            return nearest;
        }
    }
}
