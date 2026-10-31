using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// The LATE CHARGE of a holding line, shared by RBMBehaviorHoldChoke (Line role) and RBMBehaviorHedgehog (Infantry role).
    /// User rule: defenders charge AS LATE AS POSSIBLE. The line keeps its Move order, place, facing, width and arrangement and
    /// FIGHTS IN PLACE from its slots (under a Move order RBM holds every man to his slot; Frontline charge logic stays off);
    /// it charges only on a late trigger. One instance per behavior, AI thread only.
    ///
    /// States (FightState): Holding -> Contact -> Charge (latched per assignment; the owning tactic then gives the engage
    /// behaviors). Contact starts at ContactMeleeShare in melee or an enemy FOOT formation SIGNIFICANT to the line (ThreatSignificance)
    /// within ContactStartGap; ends ("contactLost") after ContactLostSeconds with no melee and no such foot within ContactGap.
    /// Enemy cavalry never starts contact or lets the line charge: advantage needs enemy foot in contact (cavalry still counts on the
    /// enemy side of the ratio), broken counts foot only. Horse archers are ignored.
    ///
    /// Charge triggers (logged as the reason):
    ///  - lineGone: under LineGoneShare of the most men since the assignment, or under LineGoneMen (any state);
    ///  - pushedBack: once held, centre more than PushedBackDistance behind the hold point (along the facing);
    ///  - behind: once held, a significant enemy formation (>= BehindMinMen, no horse archers; the hedgehog counts foot only) with its
    ///    centre more than BehindDistance past the rear plane. Foot RANGED counts only within BehindRangedGap. LOOP GUARD: the
    ///    formation a local behind / infantry charge went after cannot trigger behind again for LoopGuardSeconds unless within
    ///    LoopGuardCloseGap. Behind a SQUARE (facing never turns) only within BehindSquareGap or closing at ClosingSpeed;
    ///  - the behaviors' own: infantry (InfantryReason: significant enemy foot within ContactStartGap AND the line >= ContactMeleeShare
    ///    in melee or that formation closing at ClosingSpeed), allyFighting (AllyFightingReason: a friend within LateAllyFightGap in melee
    ///    with enemy foot significant to the line; the hedgehog only without significant melee cavalry within RangedCavalryWait),
    ///    outshot (OutshotReason) and the hedgehog's cavalryGone + enemy foot in contact;
    ///  - advantage (Contact only, significant foot in contact): power ratio >= LateChargeAdvantage for LateChargeAdvantageSeconds, native
    ///    LocalPowerRatio's formula sqrt((ours + 1) / (theirs + 1)) over cached FormationPower of the line and ALL enemy formations
    ///    within ContactGap (native's own runs an engine proximity query: not on the AI thread);
    ///  - broken (Contact only): significant enemy foot formations met since Contact began lost >= LateChargeRouting of their most men
    ///    (Retreat order = all lost; panicked men leave the formation at once, so losses are the measure).
    ///
    /// The re-form after a local charge (IsLocalChargeReason) is the owning tactics' CheckRecall: RecallSeconds without significant enemy
    /// foot within RecallFootGap, at once on the pursuit leash (> LeashDistance from the charge start and under ContactMeleeShare), and
    /// for the hedgehog when significant melee cavalry comes within CavalryRecallDistance out of melee. After a recall the assignment is
    /// QUIET for RecallQuietSeconds (Reset's quietUntil): only melee, lineGone or allyFighting can charge. RecoverReason: a latched
    /// non-local engage holds again once reinforcements brought the line back.
    ///
    /// THREADING: AI thread only: cached formation data, query systems, managed melee-hit times; no engine query, no WorldPosition,
    /// no MBRandom. All state is instance fields of the owning behavior.
    /// </summary>
    internal sealed class HoldLateCharge
    {
        internal enum FightState
        {
            Holding,
            Contact,
            Charge
        }


        // Gaps are edge gaps in m; see the class comment for how the triggers use them.
        internal const float LateChargeAdvantage = 1.6f; // power ratio (ReservePlug's formula)
        internal const float LateChargeAdvantageSeconds = 8f;
        internal const float LateChargeRouting = 0.4f; // share of their most men lost (killed, wounded or fled)
        private const int RoutingMinMen = 10;
        internal const float BehindDistance = 20f; // centre past the rear plane
        private const int BehindMinMen = 10;
        internal const float BehindRangedGap = 30f; // archers farther off are not worth a charge
        internal const float BehindSquareGap = 60f; // hedgehog: foot counts only this close, or closing at ClosingSpeed
        internal const float LoopGuardSeconds = 60f;
        internal const float LoopGuardCloseGap = 15f;
        internal const float PushedBackDistance = 30f; // line centre behind its hold point, along the facing
        internal const float LineGoneShare = 0.25f; // of its most men
        internal const int LineGoneMen = 20;
        internal const float ContactGap = 30f;
        internal const float ContactMeleeShare = 0.1f;
        internal const float ContactStartGap = 15f;
        internal const float ContactLostSeconds = 10f;
        private const float MeleeWindow = 6f; // s, FormationFightingInMelee's window

        // re-form after a local charge (CheckRecall)
        internal const float RecallSeconds = 10f; // without enemy foot within RecallFootGap
        internal const float RecallMinCharge = 15f; // s
        internal const float RecallFootGap = 60f;
        internal const int RecallFootMinMen = 5;
        internal const float LeashDistance = 100f; // m from the charge start, out of melee: re-form at once
        internal const float CavalryRecallDistance = 100f; // hedgehog: significant melee cavalry, out of melee
        internal const float RecallQuietSeconds = 25f; // after a recall only melee / lineGone can charge: no bait yo-yo
        internal const float ClosingSpeed = 1f; // m/s, cached velocity toward the line

        // recovery of a latched non-local engage (RecoverReason)
        internal const int RecoverMinMen = 40;
        internal const float RecoverPeakShare = 0.5f;
        internal const int RecoverGainMen = 20; // more than when it engaged

        // outshot: the one charge at an enemy that stands back and shoots (OutshotReason)
        internal const float OutshotPowerRatio = 2.5f; // enemy ranged + HA FormationPower x ours
        internal const float OutshotMinMissileMen = 30f;
        internal const float OutshotHitRatio = 0.05f; // UnderRangedAttackRatio: damaging hits only, shield blocks never count
        internal const float OutshotHitGap = 15f; // s without hits restarts the window
        internal const float OutshotSeconds = 60f;
        internal const float OutshotLossShare = 0.08f; // of the men at window start
        internal const int OutshotMinLost = 10;
        internal const float OutshotStandoff = 150f; // a significant melee formation closing within this: wait for it (never HA)
        internal const float OutshotNearGap = 60f; // ... or this close, closing or not

        /// <summary>User rule: wait for cavalry, charge infantry. lineGone, outshot and cavalryGone stay charged.</summary>
        internal static bool IsLocalChargeReason(string word)
        {
            return word == "infantry" || word == "advantage" || word == "broken" || word == "behind" || word == "pushedBack" || word == "allyFighting";
        }

        // allyFighting: the line goes to a friendly formation fighting enemy foot next to it

        internal const float AllyFightMeleeShare = 0.1f;

        internal const float LateAllyFightGap = 60f; // the choke's / hedgehog's late trigger

        /// <summary>AI thread, cached data and managed melee-hit times only. A friend within maxAllyGap fights enemy foot significant to the LINE; reason text or null, the friend in answered (ReactionMonitor's handshake). waitForCavalry (hedgehog): null while significant melee cavalry is within RangedCavalryWait.</summary>
        internal static string AllyFightingReason(Mission mission, Formation line, float maxAllyGap, bool waitForCavalry, out Formation answered)
        {
            answered = null;
            if (mission == null || line == null || line.Team == null || line.CountOfUnits <= 0)
            {
                return null;
            }
            Footprint lineShape;
            if (!FormationGeometry.TryGet(line, FootprintMode.ScatterAware, out lineShape))
            {
                return null;
            }
            var side = line.Team.Side;
            foreach (Team team in mission.Teams)
            {
                if (team == null || team.Side != side)
                {
                    continue;
                }
                foreach (Formation ally in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (ally == null || ally == line || ally.CountOfUnits <= 0)
                    {
                        continue;
                    }
                    Footprint allyShape;
                    if (!FormationGeometry.TryGet(ally, FootprintMode.ScatterAware, out allyShape))
                    {
                        continue;
                    }
                    float allyGap = FormationGeometry.Gap(lineShape, allyShape);
                    if (allyGap > maxAllyGap)
                    {
                        continue;
                    }
                    Formation foe = null;
                    float foeGap = float.MaxValue;
                    foreach (Team enemyTeam in mission.Teams)
                    {
                        if (enemyTeam == null || !enemyTeam.IsEnemyOf(line.Team))
                        {
                            continue;
                        }
                        foreach (Formation e in enemyTeam.FormationsIncludingSpecialAndEmpty)
                        {
                            if (e == null || e.CountOfUnits <= 0 || e.QuerySystem.IsCavalryFormation || e.QuerySystem.IsRangedCavalryFormation)
                            {
                                continue;
                            }
                            Footprint foeShape;
                            if (!FormationGeometry.TryGet(e, FootprintMode.ScatterAware, out foeShape))
                            {
                                continue;
                            }
                            float gap = FormationGeometry.Gap(foeShape, allyShape);
                            if (gap <= ContactGap && gap < foeGap && ThreatSignificance.IsSignificant(line, e))
                            {
                                foeGap = gap;
                                foe = e;
                            }
                        }
                    }
                    if (foe == null)
                    {
                        continue;
                    }
                    float melee = MeleeShare(ally);
                    if (melee < AllyFightMeleeShare)
                    {
                        continue;
                    }
                    if (waitForCavalry)
                    {
                        float cavalryGap;
                        if (NearestSignificantEnemyCavalry(mission, line, out cavalryGap) != null && cavalryGap <= RangedCavalryWait)
                        {
                            return null;
                        }
                    }
                    answered = ally;
                    return "allyFighting " + TerrainCavalry.FormationName(ally) + " melee=" + Fmt2(melee) + " vs " + ThreatSignificance.Describe(line, foe)
                        + " allyGap=" + FormationGeometry.GapText(allyGap)
                        + " enemyGap=" + FormationGeometry.GapText(foeGap);
                }
            }
            return null;
        }

        /// <summary>The behaviors' own "infantry" trigger (after Read); else null.</summary>
        internal string InfantryReason(Mission mission = null, bool rangedWaitsForCavalry = false)
        {
            if (!(_nearestSignificantGap <= ContactStartGap))
            {
                return null;
            }
            if (_meleeShare < ContactMeleeShare && _nearestSignificantClosing < ClosingSpeed)
            {
                return null;
            }
            // hedgehog (rangedWaitsForCavalry): enemy ARCHERS walking up while significant melee cavalry is within RangedCavalryWait are bait - the square waits; melee foot is always charged
            if (rangedWaitsForCavalry && mission != null && _nearestSignificant != null && _nearestSignificant.QuerySystem.IsRangedFormation)
            {
                float cavalryGap;
                if (NearestSignificantEnemyCavalry(mission, _readLine, out cavalryGap) != null && cavalryGap <= RangedCavalryWait)
                {
                    return null;
                }
            }
            return "infantry gap=" + FormationGeometry.GapText(_nearestSignificantGap) + " closing=" + Fmt1(_nearestSignificantClosing) + "m/s melee=" + Fmt2(_meleeShare)
                + " " + ThreatSignificance.Describe(_readLine, _nearestSignificant);
        }

        /// <summary>AI thread, from the tactics while a NON-local engage is latched: the reason to hold again, or null.</summary>
        internal static string RecoverReason(Mission mission, Formation line, int peak, int menAtEngage)
        {
            if (mission == null || line == null)
            {
                return null;
            }
            int men = line.CountOfUnits;
            float need = MathF.Max((float)RecoverMinMen, RecoverPeakShare * peak);
            if (men < need || men < menAtEngage + RecoverGainMen)
            {
                return null;
            }
            float foot = NearestEnemyFootGap(mission, line);
            if (foot <= ContactGap)
            {
                return null;
            }
            float melee = MeleeShare(line);
            if (melee >= ContactMeleeShare)
            {
                return null;
            }
            return "recover men=" + men.ToString(CultureInfo.InvariantCulture) + " (engaged at " + menAtEngage.ToString(CultureInfo.InvariantCulture)
                + ", peak " + peak.ToString(CultureInfo.InvariantCulture) + ", need " + Fmt0(need) + ") melee=" + Fmt2(melee)
                + " foot=" + FormationGeometry.GapText(foot);
        }

        /// <summary>AI thread: calm = no significant enemy foot within RecallFootGap, under ContactMeleeShare in melee and, with noRanged (an out-shot engage), no enemy ranged within OutshotStandoff; else null. The callers time it.</summary>
        internal static string CalmReason(Mission mission, Formation line, bool noRanged)
        {
            if (mission == null || line == null || line.CountOfUnits <= 0)
            {
                return null;
            }
            float foot = NearestEnemyFootGap(mission, line);
            if (foot <= RecallFootGap)
            {
                return null;
            }
            float ranged = noRanged ? NearestEnemyRangedGap(mission, line) : float.MaxValue;
            if (ranged <= OutshotStandoff)
            {
                return null;
            }
            float melee = MeleeShare(line);
            if (melee >= ContactMeleeShare)
            {
                return null;
            }
            return "foot=" + FormationGeometry.GapText(foot) + " melee=" + Fmt2(melee)
                + (noRanged ? " ranged=" + FormationGeometry.GapText(ranged) : "");
        }

        /// <summary>InfantryReason with rangedWaitsForCavalry: significant melee cavalry this close keeps the line from charging archers.</summary>
        internal const float RangedCavalryWait = 150f;

        private enum NearestKind
        {
            Foot,
            Ranged,
            Cavalry
        }

        /// <summary>AI thread: edge gap (m, footprints) to the nearest significant enemy foot (>= RecallFootMinMen men, no cavalry / HA); MaxValue without one.</summary>
        internal static float NearestEnemyFootGap(Mission mission, Formation line)
        {
            float gap;
            Nearest(mission, line, NearestKind.Foot, out gap);
            return gap;
        }

        /// <summary>AI thread: edge gap (m, footprints) to the nearest significant enemy ranged (foot or HA); MaxValue without one.</summary>
        internal static float NearestEnemyRangedGap(Mission mission, Formation line)
        {
            float gap;
            Nearest(mission, line, NearestKind.Ranged, out gap);
            return gap;
        }

        /// <summary>AI thread: nearest significant enemy MELEE cavalry (no HA) and its edge gap in gap; null (MaxValue) without one.</summary>
        internal static Formation NearestSignificantEnemyCavalry(Mission mission, Formation line, out float gap)
        {
            return Nearest(mission, line, NearestKind.Cavalry, out gap);
        }

        private static Formation Nearest(Mission mission, Formation line, NearestKind kind, out float gap)
        {
            gap = float.MaxValue;
            Formation best = null;
            Footprint lineShape;
            if (mission == null || line == null || line.Team == null || !FormationGeometry.TryGet(line, FootprintMode.ScatterAware, out lineShape))
            {
                return null;
            }
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(line.Team))
                {
                    continue;
                }
                foreach (Formation e in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (e == null || e.CountOfUnits <= 0)
                    {
                        continue;
                    }
                    FormationQuerySystem q = e.QuerySystem;
                    bool wanted;
                    switch (kind)
                    {
                        case NearestKind.Foot:
                            wanted = e.CountOfUnits >= RecallFootMinMen && !q.IsCavalryFormation && !q.IsRangedCavalryFormation;
                            break;
                        case NearestKind.Ranged:
                            wanted = q.IsRangedCavalryFormation || (!q.IsCavalryFormation && RBMAI.Utilities.IsRangedForArrangement(e));
                            break;
                        default:
                            wanted = q.IsCavalryFormation && !q.IsRangedCavalryFormation;
                            break;
                    }
                    Footprint shape;
                    if (!wanted || !FormationGeometry.TryGet(e, FootprintMode.ScatterAware, out shape))
                    {
                        continue;
                    }
                    float g = FormationGeometry.Gap(lineShape, shape);
                    if (g < gap && ThreatSignificance.IsSignificant(line, e))
                    {
                        gap = g;
                        best = e;
                    }
                }
            }
            return best;
        }

        private FightState _state = FightState.Holding;
        private float _lastContact = float.NaN;
        private float _advantageSince = float.NaN;
        private int _linePeak;
        private readonly Dictionary<Formation, int> _contactPeak = new Dictionary<Formation, int>();
        private float _outshotSince = float.NaN;
        private float _outshotLastHit = float.NaN;
        private int _outshotStartMen;

        // the last Read (AI thread)
        private float _contactPower;
        /// <summary>Nearest significant enemy foot: Contact by distance and the "infantry" trigger.</summary>
        private float _nearestSignificantGap;
        private Formation _nearestSignificant;
        private float _nearestSignificantClosing; // m/s
        private Formation _readLine;
        private bool _footInContact;
        private float _meleeShare;
        private string _behind;
        /// <summary>The latched charge's target.</summary>
        private Formation _behindFormation;
        /// <summary>Null for reasons other than behind / infantry.</summary>
        private Formation _chargeTarget;
        /// <summary>Loop guard, NOT cleared by Reset: cannot set off behind until _guardUntil unless within LoopGuardCloseGap.</summary>
        private Formation _guardFormation;
        private float _guardUntil = float.NegativeInfinity;
        /// <summary>Until then only melee / lineGone can charge.</summary>
        private float _quietUntil = float.NegativeInfinity;

        internal FightState State => _state;

        internal float LastMeleeShare => _meleeShare;

        /// <summary>Set by the behavior once the line has stood at its point (pushedBack / behind need a held position).</summary>
        internal bool ReachedHold;

        /// <summary>The archer stand: only broken (and the owner's own reason) charges; contact is tracked as usual.</summary>
        internal bool BrokenOnly;

        /// <summary>A SIGNIFICANT enemy foot formation (no cavalry / HA) within ContactGap at the last read.</summary>
        internal bool FootInContact => _footInContact;

        /// <summary>The decision label's word once latched, else null.</summary>
        internal string ChargeReason { get; private set; }

        /// <summary>quietUntil = mission time until which only melee / lineGone can charge; NegativeInfinity = none.</summary>
        internal void Reset(Formation line, float quietUntil = float.NegativeInfinity)
        {
            // re-form after a local behind / infantry charge: its target is guarded against setting the line off again (LoopGuardSeconds)
            if (!float.IsNegativeInfinity(quietUntil) && _state == FightState.Charge && _chargeTarget != null
                && (ChargeReason == "behind" || ChargeReason == "infantry"))
            {
                _guardFormation = _chargeTarget;
                _guardUntil = quietUntil - RecallQuietSeconds + LoopGuardSeconds;
            }
            _chargeTarget = null;
            _behindFormation = null;
            _state = FightState.Holding;
            _lastContact = float.NaN;
            _advantageSince = float.NaN;
            _linePeak = (line != null) ? line.CountOfUnits : 0;
            _contactPeak.Clear();
            _contactPower = 0f;
            _nearestSignificantGap = float.MaxValue;
            _nearestSignificant = null;
            _nearestSignificantClosing = 0f;
            _quietUntil = quietUntil;
            _readLine = null;
            _footInContact = false;
            _meleeShare = 0f;
            _behind = null;
            ReachedHold = false;
            ChargeReason = null;
            _outshotSince = float.NaN;
            _outshotLastHit = float.NaN;
            _outshotStartMen = 0;
        }

        /// <summary>Outshot trigger (user rule: enemy ranged power much higher AND hits actually landing), or null. Only Holding at its point with no significant melee enemy coming in. Any failing condition restarts the window. AI thread, cached data only.</summary>
        internal string OutshotReason(Mission mission, Formation line, float now)
        {
            if (_state != FightState.Holding || !ReachedHold || mission == null || line == null || line.Team == null || line.CountOfUnits <= 0)
            {
                ResetOutshot();
                return null;
            }
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(line);
            Footprint lineShape;
            if (!centre.IsValid || !FormationGeometry.TryGet(line, FootprintMode.Current, out lineShape))
            {
                return null;
            }
            float ourPower = 0f;
            float theirPower = 0f;
            float nearestFighter = float.MaxValue;
            Formation nearestFighterFormation = null;
            bool cavalryNear = false;
            foreach (Team team in mission.Teams)
            {
                if (team == null)
                {
                    continue;
                }
                bool enemy = team.IsEnemyOf(line.Team);
                if (!enemy && team != line.Team && !team.IsFriendOf(line.Team))
                {
                    continue;
                }
                foreach (Formation f in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (f == null || f.CountOfUnits <= 0)
                    {
                        continue;
                    }
                    bool ranged = f.QuerySystem.IsRangedFormation || f.QuerySystem.IsRangedCavalryFormation;
                    if (!enemy)
                    {
                        if (ranged)
                        {
                            ourPower += f.QuerySystem.FormationPower;
                        }
                        continue;
                    }
                    if (ranged)
                    {
                        theirPower += f.QuerySystem.FormationPower;
                        // archers and horse archers are what the window measures, never a reason to wait
                        continue;
                    }
                    bool mounted = f.QuerySystem.IsCavalryFormation;
                    if (!ThreatSignificance.IsSignificant(line, f))
                    {
                        continue;
                    }
                    Vec2 c = RBMAI.Utilities.GetFormationCenter(f);
                    Footprint shape;
                    if (!c.IsValid || !FormationGeometry.TryGet(f, FootprintMode.ScatterAware, out shape))
                    {
                        continue;
                    }
                    Vec2 toLine = centre - c;
                    float distance = toLine.Normalize();
                    float gap = FormationGeometry.Gap(lineShape, shape);
                    float closing = (distance > 0.01f) ? Vec2.DotProduct(EnemyMotion.Velocity(f), toLine) : 0f;
                    // coming in or already close: wait for it
                    if (!(gap <= OutshotNearGap || (gap <= OutshotStandoff && closing >= ClosingSpeed)))
                    {
                        continue;
                    }
                    if (mounted)
                    {
                        cavalryNear = true;
                    }
                    else if (gap < nearestFighter)
                    {
                        nearestFighter = gap;
                        nearestFighterFormation = f;
                    }
                }
            }
            TeamQuerySystem q = line.Team.QuerySystem;
            float theirMissiles = (q.EnemyRangedRatio + q.EnemyRangedCavalryRatio) * q.EnemyUnitCount;
            float hit = line.QuerySystem.UnderRangedAttackRatio;
            if (cavalryNear || nearestFighter <= OutshotStandoff || theirMissiles < OutshotMinMissileMen || theirPower < OutshotPowerRatio * ourPower)
            {
                ResetOutshot();
                return null;
            }
            if (hit >= OutshotHitRatio)
            {
                _outshotLastHit = now;
            }
            if (float.IsNaN(_outshotSince))
            {
                if (hit >= OutshotHitRatio)
                {
                    _outshotSince = now;
                    _outshotStartMen = line.CountOfUnits;
                }
                return null;
            }
            if (now - _outshotLastHit > OutshotHitGap)
            {
                ResetOutshot();
                return null;
            }
            int lost = _outshotStartMen - line.CountOfUnits;
            // an enemy with (almost) no melee foot never comes in: shorter window, no loss needed (kFoot)
            float kFoot = ArmyComposition.KFoot(line.Team);
            float window = ArmyComposition.Lerp(OutshotSeconds, ArmyComposition.OutshotSecondsNoFoot, kFoot);
            float need = MathF.Max((float)OutshotMinLost, OutshotLossShare * _outshotStartMen) * (1f - kFoot);
            if (now - _outshotSince >= window && lost >= need)
            {
                return "outshot rangedPower=" + Fmt0(theirPower) + " vs " + Fmt0(ourPower)
                    + ((kFoot > 0f) ? " kFoot=" + Fmt2(kFoot) + " window=" + Fmt0(window) + "s need=" + Fmt0(need) : "")
                    + " missiles=" + Fmt0(theirMissiles) + " hit=" + Fmt2(hit) + " lost=" + lost.ToString(CultureInfo.InvariantCulture)
                    + " of " + _outshotStartMen.ToString(CultureInfo.InvariantCulture) + " in " + Fmt0(now - _outshotSince) + "s"
                    + " fighters=" + FormationGeometry.GapText(nearestFighter);
            }
            return null;
        }

        private void ResetOutshot()
        {
            _outshotSince = float.NaN;
            _outshotLastHit = float.NaN;
            _outshotStartMen = 0;
        }

        /// <summary>AI thread. square = square / circle (edge gaps to its size); behindFootOnly = only foot counts as "behind".</summary>
        internal void Read(Mission mission, Formation line, Vec2 facing, bool square, bool behindFootOnly)
        {
            _contactPower = 0f;
            _nearestSignificantGap = float.MaxValue;
            _nearestSignificant = null;
            _nearestSignificantClosing = 0f;
            _readLine = line;
            _footInContact = false;
            _behind = null;
            _behindFormation = null;
            if (mission == null || line == null || line.Team == null || line.CountOfUnits <= 0)
            {
                return;
            }
            float now = mission.CurrentTime;
            if (line.CountOfUnits > _linePeak)
            {
                _linePeak = line.CountOfUnits;
            }
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(line);
            if (!centre.IsValid)
            {
                return;
            }
            if (!facing.IsValid || facing.LengthSquared < 0.01f)
            {
                facing = line.Direction;
            }
            if (!facing.IsValid || facing.LengthSquared < 0.01f)
            {
                facing = new Vec2(0f, 1f);
            }
            facing = facing.Normalized();
            float halfDepth = square ? MathF.Max(line.Width, line.Depth) * 0.5f : line.Depth * 0.5f;
            // the line's shape at the facing it holds; a circle is a disc
            Footprint lineShape = (square && line.ArrangementOrder.OrderEnum == ArrangementOrder.ArrangementOrderEnum.Circle)
                ? Footprint.Circle(centre, halfDepth)
                : Footprint.Box(centre, facing, MathF.Max(line.Width, 1f) * 0.5f, MathF.Max(line.Depth, 1f) * 0.5f);
            float behindBy = 0f;
            // archers are the enemy's army when it has (almost) no melee foot: catch them farther out (kFoot)
            float behindKFoot = ArmyComposition.KFoot(line.Team);
            float behindRangedGap = ArmyComposition.Lerp(BehindRangedGap, ArmyComposition.BehindRangedGapNoFoot, behindKFoot);
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(line.Team))
                {
                    continue;
                }
                foreach (Formation e in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (e == null || e.CountOfUnits <= 0 || e.QuerySystem.IsRangedCavalryFormation)
                    {
                        continue;
                    }
                    Vec2 c = RBMAI.Utilities.GetFormationCenter(e);
                    Footprint shape;
                    if (!c.IsValid || !FormationGeometry.TryGet(e, FootprintMode.ScatterAware, out shape))
                    {
                        continue;
                    }
                    Vec2 rel = c - centre;
                    float gap = FormationGeometry.Gap(lineShape, shape);
                    bool mounted = e.QuerySystem.IsCavalryFormation;
                    // significant to the line (anti-bait): only these are a reason to charge and start / hold Contact by distance; a small group fighting the line still starts it through the melee share
                    bool significant = ThreatSignificance.IsSignificant(line, e);
                    // cavalry never starts or holds contact; it does count against the line's power
                    if (!mounted && significant && gap < _nearestSignificantGap)
                    {
                        _nearestSignificantGap = gap;
                        _nearestSignificant = e;
                        float distance = rel.Length;
                        _nearestSignificantClosing = (distance > 0.01f) ? -Vec2.DotProduct(EnemyMotion.Velocity(e), rel) / distance : 0f;
                    }
                    if (gap <= ContactGap)
                    {
                        // enemy side of the ratio: everything in contact
                        _contactPower += e.QuerySystem.FormationPower;
                        if (!mounted && significant)
                        {
                            _footInContact = true;
                        }
                        if (_state == FightState.Contact && !mounted && significant)
                        {
                            int peak;
                            if (!_contactPeak.TryGetValue(e, out peak) || e.CountOfUnits > peak)
                            {
                                _contactPeak[e] = e.CountOfUnits;
                            }
                        }
                    }
                    // past the rear plane (the enemy's centre); foot ranged only when catchable (BehindRangedGap); loop guard for a just-recalled target
                    // square / circle (frozen facing): only within BehindSquareGap or closing
                    float past = -Vec2.DotProduct(rel, facing) - halfDepth;
                    bool squareReach = !square || gap <= BehindSquareGap;
                    if (!squareReach && past > BehindDistance)
                    {
                        float d = rel.Length;
                        squareReach = d > 0.01f && -Vec2.DotProduct(EnemyMotion.Velocity(e), rel) / d >= ClosingSpeed;
                    }
                    if (past > BehindDistance && past > behindBy && e.CountOfUnits >= BehindMinMen && (!behindFootOnly || !mounted)
                        && significant && squareReach
                        && (mounted || gap <= behindRangedGap || !RBMAI.Utilities.IsRangedForArrangement(e))
                        && (e != _guardFormation || now >= _guardUntil || gap <= LoopGuardCloseGap))
                    {
                        behindBy = past;
                        _behindFormation = e;
                        _behind = ThreatSignificance.Describe(line, e) + " by " + Fmt0(past) + "m gap=" + FormationGeometry.GapText(gap)
                            + ((gap > BehindRangedGap && !mounted && RBMAI.Utilities.IsRangedForArrangement(e)) ? " kFoot=" + Fmt2(behindKFoot) + " rangedGap=" + Fmt0(behindRangedGap) : "");
                    }
                }
            }
            _meleeShare = MeleeShare(line);
        }

        /// <summary>AI thread, after Read: returns the transition ("Holding->Contact", ...) with its reason in why, or null. ownReason = the behavior's own trigger this tick.</summary>
        internal string Decide(Formation line, float now, Vec2 holdPoint, Vec2 facing, string ownReason, out string why)
        {
            why = null;
            if (_state == FightState.Charge || line == null || line.CountOfUnits <= 0)
            {
                return null;
            }
            FightState old = _state;
            bool touching = _meleeShare >= ContactMeleeShare || _nearestSignificantGap <= ContactGap;
            if (_state == FightState.Holding && (_meleeShare >= ContactMeleeShare || _nearestSignificantGap <= ContactStartGap))
            {
                _state = FightState.Contact;
                _lastContact = now;
                _advantageSince = float.NaN;
                _contactPeak.Clear();
                why = (_meleeShare >= ContactMeleeShare ? "melee=" + Fmt2(_meleeShare) : "enemyNear gap=" + FormationGeometry.GapText(_nearestSignificantGap))
                    + " power=" + Fmt2(PowerRatio(line));
                // a trigger that already holds latches at once: Holding->Charge
                string chargeNow = ChargeReasonNow(line, now, holdPoint, facing, ownReason);
                if (chargeNow != null)
                {
                    return Latch(chargeNow, old, out why);
                }
                return old + "->" + _state;
            }
            if (_state == FightState.Contact)
            {
                if (touching)
                {
                    _lastContact = now;
                }
                else if (now - _lastContact >= ContactLostSeconds)
                {
                    _state = FightState.Holding;
                    _advantageSince = float.NaN;
                    _contactPeak.Clear();
                    why = "contactLost " + Fmt0(now - _lastContact) + "s";
                    return old + "->" + _state;
                }
            }
            string reason = ChargeReasonNow(line, now, holdPoint, facing, ownReason);
            return (reason != null) ? Latch(reason, old, out why) : null;
        }

        private string Latch(string reason, FightState old, out string why)
        {
            _state = FightState.Charge;
            ChargeReason = reason.Split(' ')[0];
            _chargeTarget = (ChargeReason == "behind") ? _behindFormation : (ChargeReason == "infantry") ? _nearestSignificant : null;
            why = reason;
            return old + "->" + FightState.Charge;
        }

        private string ChargeReasonNow(Formation line, float now, Vec2 holdPoint, Vec2 facing, string ownReason)
        {
            int men = line.CountOfUnits;
            if (!BrokenOnly && (men < LineGoneMen || (_linePeak > 0 && men < LineGoneShare * _linePeak)))
            {
                return "lineGone men=" + men.ToString(CultureInfo.InvariantCulture) + " of " + _linePeak.ToString(CultureInfo.InvariantCulture);
            }
            // quiet after a recall: only melee, or a friend fighting significant enemy foot next to it (allyFighting is no bait)
            if (now < _quietUntil && _meleeShare < ContactMeleeShare)
            {
                _advantageSince = float.NaN;
                return (ownReason != null && ownReason.StartsWith("allyFighting")) ? ownReason : null;
            }
            if (ReachedHold && !BrokenOnly)
            {
                Vec2 centre = RBMAI.Utilities.GetFormationCenter(line);
                if (centre.IsValid && holdPoint.IsValid && facing.IsValid && facing.LengthSquared > 0.01f)
                {
                    float back = -Vec2.DotProduct(centre - holdPoint, facing.Normalized());
                    if (back > PushedBackDistance)
                    {
                        return "pushedBack " + Fmt0(back) + "m behind the point";
                    }
                }
                if (_behind != null)
                {
                    return "behind " + _behind;
                }
            }
            if (ownReason != null)
            {
                return ownReason;
            }
            if (_state != FightState.Contact)
            {
                _advantageSince = float.NaN;
                return null;
            }
            float ratio = PowerRatio(line);
            if (!BrokenOnly && _footInContact && _contactPower > 0f && ratio >= LateChargeAdvantage)
            {
                if (float.IsNaN(_advantageSince))
                {
                    _advantageSince = now;
                }
                if (now - _advantageSince >= LateChargeAdvantageSeconds)
                {
                    return "advantage power=" + Fmt2(ratio) + " for " + Fmt0(now - _advantageSince) + "s";
                }
            }
            else
            {
                _advantageSince = float.NaN;
            }
            int peakSum = 0;
            int lostSum = 0;
            foreach (KeyValuePair<Formation, int> kv in _contactPeak)
            {
                Formation e = kv.Key;
                int peak = kv.Value;
                int count = (e != null) ? e.CountOfUnits : 0;
                bool retreating = e != null && count > 0 && e.GetReadonlyMovementOrderReference().OrderType == OrderType.Retreat;
                peakSum += peak;
                lostSum += retreating ? peak : System.Math.Max(0, peak - count);
            }
            if (peakSum >= RoutingMinMen && lostSum >= LateChargeRouting * peakSum)
            {
                return "broken lost=" + Fmt2((float)lostSum / peakSum) + " of " + peakSum.ToString(CultureInfo.InvariantCulture)
                    + " men in " + _contactPeak.Count.ToString(CultureInfo.InvariantCulture) + " formations";
            }
            return null;
        }

        /// <summary>Native LocalPowerRatio's formula over cached powers (RBMBehaviorReservePlug's re-implementation); 1 with nobody in contact.</summary>
        private float PowerRatio(Formation line)
        {
            if (_contactPower <= 0f)
            {
                return 1f;
            }
            return MathF.Sqrt((line.QuerySystem.FormationPower + 1f) / (_contactPower + 1f));
        }

        /// <summary>AI thread: share of the line that dealt or took a melee hit within MeleeWindow (every man counted).</summary>
        internal static float MeleeShare(Formation line)
        {
            int count = (line != null) ? line.CountOfUnits : 0;
            if (count <= 0)
            {
                return 0f;
            }
            float time = MBCommon.GetTotalMissionTime();
            MeleeShareEntry cached;
            if (MeleeShareCache.TryGetValue(line, out cached) && cached.Time == time && cached.Count == count)
            {
                return cached.Share;
            }
            int fighting = 0;
            line.ApplyActionOnEachUnitViaBackupList(delegate (Agent agent)
            {
                if (agent != null && (time - agent.LastMeleeHitTime < MeleeWindow || time - agent.LastRecievedMeleeHitTime < MeleeWindow))
                {
                    fighting++;
                }
            });
            float share = (float)fighting / count;
            MeleeShareCache[line] = new MeleeShareEntry(time, count, share);
            return share;
        }

        /// <summary>Perf cache: one MeleeShare per formation per mission-time stamp and unit count. Immutable entries replaced whole (AI and main thread). Cleared in MissionStartReset.</summary>
        private sealed class MeleeShareEntry
        {
            public readonly float Time;
            public readonly int Count;
            public readonly float Share;

            public MeleeShareEntry(float time, int count, float share)
            {
                Time = time;
                Count = count;
                Share = share;
            }
        }

        private static readonly ConcurrentDictionary<Formation, MeleeShareEntry> MeleeShareCache = new ConcurrentDictionary<Formation, MeleeShareEntry>();

        internal static void ClearMeleeShareCache()
        {
            MeleeShareCache.Clear();
        }

        /// <summary>"contact", "charge advantage", or null while holding.</summary>
        internal string Label()
        {
            switch (_state)
            {
                case FightState.Contact:
                    return "contact";
                case FightState.Charge:
                    return "charge " + (ChargeReason ?? "-");
                default:
                    return null;
            }
        }

        private static string Fmt0(float value)
        {
            return value.ToString("0", CultureInfo.InvariantCulture);
        }

        private static string Fmt1(float value)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string Fmt2(float value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// The recall rules after a hold's engage, shared by the choke hold, the hedgehog and the battle position (each owner keeps
    /// its own policy: leash and return distances, what happens beyond them, its gates). Leash: a LOCAL charge more than the
    /// owner's leash from where it started, out of melee, no significant enemy foot within ContactGap = at once. No foot: no
    /// significant enemy foot within RecallFootGap for RecallSeconds. Calm: CalmReason for RecallSeconds. Both timed rules only
    /// after RecallMinCharge, each with its own timer (a rule not asked this tick is reset by its owner). AI thread, one per owner.
    /// </summary>
    internal sealed class HoldRecall
    {
        private float _noFootSince = float.NaN;
        private float _calmSince = float.NaN;

        internal void Reset()
        {
            _noFootSince = float.NaN;
            _calmSince = float.NaN;
        }

        internal void ResetNoFoot()
        {
            _noFootSince = float.NaN;
        }

        internal void ResetCalm()
        {
            _calmSince = float.NaN;
        }

        /// <summary>The leash rule: true with the distance from the charge's start and the melee share when it recalls now.</summary>
        internal static bool Leashed(Formation line, Vec2 centre, Vec2 chargeStart, float leashDistance, float nearestFoot, out float leash, out float melee)
        {
            leash = chargeStart.IsValid ? centre.Distance(chargeStart) : 0f;
            melee = float.NaN;
            // never out of a fight it is in (significant enemy foot within ContactGap)
            if (leash <= leashDistance || nearestFoot <= HoldLateCharge.ContactGap)
            {
                return false;
            }
            melee = HoldLateCharge.MeleeShare(line);
            return melee < HoldLateCharge.ContactMeleeShare;
        }

        /// <summary>The no-foot rule: true once no significant enemy foot was within RecallFootGap for RecallSeconds (RecallMinCharge after the engage).</summary>
        internal bool NoFootFor(float now, float engagedAt, float nearestFoot)
        {
            if (now - engagedAt < HoldLateCharge.RecallMinCharge || nearestFoot <= HoldLateCharge.RecallFootGap)
            {
                _noFootSince = float.NaN;
                return false;
            }
            if (float.IsNaN(_noFootSince))
            {
                _noFootSince = now;
            }
            return now - _noFootSince >= HoldLateCharge.RecallSeconds;
        }

        /// <summary>The calm rule: the CalmReason text once the line was calm for RecallSeconds (RecallMinCharge after the engage), else null. Not calm (or not asked: calm null) restarts it.</summary>
        internal string CalmFor(float now, float engagedAt, string calm)
        {
            if (calm == null || float.IsNaN(engagedAt) || now - engagedAt < HoldLateCharge.RecallMinCharge)
            {
                _calmSince = float.NaN;
                return null;
            }
            if (float.IsNaN(_calmSince))
            {
                _calmSince = now;
            }
            return (now - _calmSince >= HoldLateCharge.RecallSeconds) ? calm : null;
        }
    }
}
