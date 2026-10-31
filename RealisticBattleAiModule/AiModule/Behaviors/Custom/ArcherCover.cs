using System.Globalization;
using System.Text;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// Defender archers take cover from a closing enemy (user rule): behind / beside friendly infantry. One instance per
    /// behavior, used by the Archer behaviors and the HoldPosition / HoldChoke Archers roles (not the hedgehog). The behavior
    /// calls Evaluate at the top of its order calculation; while it returns true it moves to the cover target facing the threat.
    ///
    /// Who: the DEFENDER's AI foot ranged formations with a published BattleTerrain.Current, against enemy melee FOOT only
    /// (stand vs mounted, user rule: cavalry / horse archers never start a cover or step, only logged). Inactive behaviors change nothing.
    /// A charging friendly line is neither screen nor cover; the engaged main the archers support (SetSupport) is, even under Charge.
    ///
    /// THREAT: nearest significant, not retreating enemy melee foot within CloseRange, or within InfantryThreatRange and closing
    /// (cached velocity or CENTRE distance shrinking: a width change is no closing speed). Gaps use RangedStableRadius (size in Line).
    ///
    /// TAKE: not when already covered (a cover formation on the threat-to-archers line) or at a covered post (AtPost /
    /// PostUncovered); at most one take/change per MinChangeSeconds; RetakeSameSeconds against the same threat;
    /// the point must gain CoverMinGain m ("noGain") and be within CoverMaxTravel.
    ///
    /// COVER POINT: friendly melee infantry (>= max(CoverMinMen, CoverMenShare x archers)), candidates behind / behindLeft /
    /// behindRight / beside by the front-centre rule (the Move point is the FRONT-centre). Refused: unwalkable, near the edge,
    /// not farther from the threat than the infantry, path passes the threat, friend overlap, another significant enemy near,
    /// too far. Best = shortest walk + KindPenalty.
    ///
    /// FALLBACK without cover: FallbackStep m away from the threat bent toward the team average, up to FallbackMaxSteps while
    /// the threat presses; refused beyond the cohesion limit ("cohesion(...)"). Nothing valid = the behavior's own order.
    ///
    /// HOLD: a copy of the cover infantry's cached median WorldPosition with ONE SetVec2MT (native caches the order position by
    /// its Vec2), rebuilt at most every RebuildMinSeconds and when moved > RebuildDistance, placed against a FROZEN threat
    /// position (moved only when its bearing turned > ~40 deg). ENGAGED (threat on the cover infantry which is in melee): point
    /// frozen, they shoot from there. RELEASE: threat gone / far and calm, cover gone, behavior ends. Arrangement: the shared
    /// ChooseRangedArrangement as a fixed post.
    ///
    /// Log: BEHAVIOR queue, point "cover" (take/change/step/engaged/disengaged/release/none/stay).
    ///
    /// THREADING: AI thread only, cached data and the immutable terrain snapshot, one SetVec2MT on a fresh copy; no engine
    /// query, no MBRandom, no statics.
    /// </summary>
    internal sealed class ArcherCover
    {

        // Distances are edge gaps in m unless noted.
        internal const float InfantryThreatRange = 70f;
        internal const float CavalryThreatRange = 90f;
        internal const float CloseRange = 35f;
        internal const float ClosingSpeed = 1f; // m/s
        internal const float CavalryClosingSpeed = 6f; // m/s, a real charge
        internal const float CavalryNearGap = 60f;
        internal const float RetakeSameSeconds = 20f; // unless within CloseRange
        internal const float FreezeTurnCos = 0.766f; // ~40 deg
        internal const int CoverMinMen = 20;
        internal const float CoverMenShare = 0.5f; // of the archers' men
        internal const float CoveredSlack = 5f; // threat-to-archers line vs the infantry's footprint
        internal const float CoveredMaxGap = 40f;
        internal const float BehindGap = 8f; // archers' front behind the infantry's rear edge
        internal const float BesideGap = 8f;

        /// <summary>Extra walk (m) a non-straight-behind candidate must save to win.</summary>
        private static readonly float[] KindPenalty = { 0f, 10f, 10f, 25f };

        internal const float ReleaseRange = 120f;
        internal const float ReleaseCalmSeconds = 8f;
        internal const float MinChangeSeconds = 6f; // a gone cover formation is replaced at once
        internal const float RebuildMinSeconds = 2f;
        internal const float RebuildDistance = 5f;
        internal const float EngagedGap = 15f;
        internal const float EngagedMeleeRatio = 0.1f;
        internal const float ArriveDistance = 15f; // + a quarter of their depth: the point is frozen
        internal const float EngagedRebuildDistance = 15f;
        internal const float PathClear = 15f; // beyond the threat's radius
        internal const float OtherEnemyMinGap = 50f;
        internal const float CoverMaxTravel = 45f;
        internal const float CoverMinGain = 15f; // farther from the threat than now
        internal const float PostMaxGap = 25f;
        internal const float PostCoverGap = 60f;
        internal const float PostLeaveSpeed = 1f; // m/s
        internal const float EdgeMin = 20f; // from the map edge
        internal const float RetargetMargin = 10f;
        internal const float FallbackStep = 40f;
        internal const int FallbackMaxSteps = 3;
        internal const float FallbackEdgeMin = 40f;
        private const float ProbeMinSeconds = 0.5f;
        private const float ProbeMaxSeconds = 5f;

        private const float Cos30 = 0.8660254f;

        private enum Kind
        {
            Behind,
            BehindLeft,
            BehindRight,
            Beside,
            Fallback
        }

        private static readonly string[] KindNames = { "behind", "behindLeft", "behindRight", "beside", "fallback" };

        private struct Candidate
        {
            public Formation Cover;
            public Kind Kind;
            public Vec2 Front;
            public Vec2 Facing;
            public float Travel;
        }

        private bool _active;
        private Kind _kind;
        private Formation _cover;
        private Formation _threat;
        private float _since = float.NaN;
        private float _changedAt = float.NegativeInfinity;
        private float _builtAt = float.NegativeInfinity;
        private float _calmSince = float.NaN;
        private bool _engaged;
        private int _steps;
        private const float ApplyKeepDistance = 0.5f; // m: Apply keeps the built point this close
        /// <summary>The cover Move point: rebuilt whenever Apply moves it (the callers gate how often).</summary>
        private readonly CachedMovePoint _move = new CachedMovePoint();
        private Vec2 _facing = Vec2.Invalid;
        private Formation _probe;
        /// <summary>CENTRE distance (a width change is no closing speed).</summary>
        private float _probeDistance;
        private float _probeAt = float.NegativeInfinity;
        // own block this tick (StableRanged)
        private Footprint _own;
        private bool _ownValid;
        private Formation _refusedThreat;
        /// <summary>Log once per threat.</summary>
        private Formation _stayThreat;
        /// <summary>Log once per formation.</summary>
        private Formation _standThreat;
        /// <summary>Frozen (FreezeTurnCos).</summary>
        private Vec2 _threatRef = Vec2.Invalid;
        private Formation _releasedThreat;
        private float _releasedAt = float.NegativeInfinity;

        /// <summary>SupportFire: the main the archers support (cover even under Charge) and its place's centre (fallback steps bend toward it).</summary>
        private Formation _supportMain;
        private Vec2 _refuge = Vec2.Invalid;

        /// <summary>AI thread, before Evaluate: the caller's SupportFire main (null: none) and its place's centre (invalid: none).</summary>
        internal void SetSupport(Formation engagedMain, Vec2 refuge)
        {
            _supportMain = engagedMain;
            _refuge = refuge;
        }

        /// <summary>AI thread: true while the archers take cover, with the cover target and facing; false = the behavior's own order. what / why = a log event this call. active = the formation's active behavior (an inactive one only being weighed changes nothing); atPost = the caller holds its fixed post: no take.</summary>
        internal bool Evaluate(Formation formation, bool active, bool atPost, out WorldPosition target, out Vec2 facing, out string what, out string why)
        {
            target = WorldPosition.Invalid;
            facing = Vec2.Invalid;
            what = null;
            why = null;
            Mission mission = Mission.Current;
            if (!active || mission == null || formation == null)
            {
                return false;
            }
            float now = mission.CurrentTime;
            try
            {
                BattleTerrain terrain = BattleTerrain.Current;
                string ineligible = (formation.Team == null) ? "noTeam"
                    : !RBMAI.Utilities.IsRangedArrangementSubject(formation) ? "notRanged"
                    : (terrain == null) ? "noTerrain" : null;
                // stand vs mounted (user rule): only enemy FOOT is a threat, only for defender archers; mounted are only logged
                bool footThreats = formation.Team != null && formation.Team.Side == BattleSideEnum.Defender;
                if (ineligible != null)
                {
                    if (_active)
                    {
                        Drop(now, ineligible, out what, out why);
                    }
                    return false;
                }
                Vec2 centre = RBMAI.Utilities.GetFormationCenter(formation);
                if (!centre.IsValid)
                {
                    return Current(out target, out facing);
                }
                // size as in Line: going Loose doubles the width and must not bring an enemy "closer"
                float ownRadius = RBMAI.Utilities.RangedStableRadius(formation);
                _ownValid = FormationGeometry.TryGet(formation, FootprintMode.StableRanged, out _own);
                float gap = float.MaxValue;
                float closing = 0f;
                Formation nearest = null;
                Formation threat = null;
                if (footThreats)
                {
                    threat = FindThreat(mission, formation, centre, ownRadius, now, out gap, out closing, out nearest);
                }
                bool covering = _active
                    ? Hold(mission, formation, terrain, centre, ownRadius, now, threat, gap, closing, out what, out why)
                    : Take(mission, formation, terrain, centre, ownRadius, now, threat, gap, closing, atPost, out what, out why);
                if (!covering && what == null)
                {
                    StandVsMounted(mission, formation, centre, ownRadius, now, out what, out why);
                }
                // gap-shrink probe: the tracked threat while covering, else the nearest melee enemy in range
                Formation probe = _active ? _threat : (threat ?? nearest);
                if (probe != null && (probe != _probe || now - _probeAt >= ProbeMinSeconds))
                {
                    float probeGap;
                    float probeClosing;
                    Vec2 probeCentre;
                    if (Measure(probe, centre, ownRadius, now, out probeGap, out probeClosing, out probeCentre))
                    {
                        _probe = probe;
                        _probeDistance = centre.Distance(probeCentre);
                        _probeAt = now;
                    }
                }
                return covering && Current(out target, out facing);
            }
            catch (System.Exception e)
            {
                // AI thread: never let an exception escape into the engine's tick (AiTickGuard)
                AiTickGuard.Report("ArcherCover", e, null);
                return _active && Current(out target, out facing);
            }
        }

        /// <summary>Ends the cover (AI thread or wherever native cancels the behavior).</summary>
        internal bool Release(float now, string reason, out string why)
        {
            string what;
            if (!_active)
            {
                why = null;
                return false;
            }
            Drop(now, reason, out what, out why);
            return true;
        }


        private bool Take(Mission mission, Formation formation, BattleTerrain terrain, Vec2 centre, float ownRadius, float now,
            Formation threat, float gap, float closing, bool atPost, out string what, out string why)
        {
            what = null;
            why = null;
            if (threat == null || now - _changedAt < MinChangeSeconds)
            {
                return false;
            }
            // not again against the threat just released from for RetakeSameSeconds unless it is close
            if (threat == _releasedThreat && now - _releasedAt < RetakeSameSeconds && gap > CloseRange)
            {
                return false;
            }
            Formation screen;
            if (Covered(mission, formation, centre, ownRadius, threat, _supportMain, out screen))
            {
                return false;
            }
            // archers at a post behind / beside their infantry stay and shoot, but only while the post IS covered (PostUncovered)
            string post = atPost ? "post" : AtPost(mission, formation, centre, _supportMain);
            string uncovered = null;
            if (post != null)
            {
                uncovered = PostUncovered(mission, formation, centre, ownRadius, threat, _supportMain);
                if (uncovered == null)
                {
                    if (threat != _stayThreat && RBMConfig.RBMConfig.battleTerrainLoggingEnabled)
                    {
                        _stayThreat = threat;
                        what = "cover stay";
                        why = post + " threat=" + ThreatSignificance.Describe(formation, threat) + " gap=" + GapText(threat, gap) + " closing=" + Fmt1(closing) + "m/s";
                    }
                    return false;
                }
                uncovered = " post uncovered(" + uncovered + ")";
            }
            bool explain = RBMConfig.RBMConfig.battleTerrainLoggingEnabled && threat != _refusedThreat;
            Candidate best;
            string refusal;
            if (!Pick(mission, formation, terrain, centre, threat, explain, out best, out refusal)
                && !Fallback(mission, formation, terrain, centre, threat, explain, out best, ref refusal))
            {
                if (explain)
                {
                    _refusedThreat = threat;
                    what = "cover none";
                    why = "threat=" + ThreatSignificance.Describe(formation, threat) + " gap=" + GapText(threat, gap) + " closing=" + Fmt1(closing) + "m/s " + refusal + (uncovered ?? "");
                }
                return false;
            }
            if (!Apply(formation, best, now))
            {
                return false;
            }
            _active = true;
            _threat = threat;
            SetThreatRef(threat);
            _since = now;
            _changedAt = now;
            _calmSince = float.NaN;
            _engaged = false;
            _steps = (best.Kind == Kind.Fallback) ? 1 : 0;
            _refusedThreat = null;
            _stayThreat = null;
            what = "cover take";
            why = Describe(best, threat, gap, closing) + (uncovered ?? "");
            return true;
        }

        private bool Hold(Mission mission, Formation formation, BattleTerrain terrain, Vec2 centre, float ownRadius, float now,
            Formation threat, float gap, float closing, out string what, out string why)
        {
            what = null;
            why = null;
            // tracked threat: gone, calm (far and not closing) or replaced by a nearer one
            float trackedGap = float.MaxValue;
            float trackedClosing = 0f;
            Vec2 trackedCentre = Vec2.Invalid;
            bool tracked = _threat != null && _threat.CountOfUnits > 0 && Measure(_threat, centre, ownRadius, now, out trackedGap, out trackedClosing, out trackedCentre);
            if (!tracked)
            {
                if (threat != null && Retarget(mission, formation, terrain, centre, now, threat, gap, closing, "threatGone", out what, out why))
                {
                    return true;
                }
                Drop(now, "threatGone dead", out what, out why);
                return false;
            }
            if (trackedGap > ReleaseRange && trackedClosing < ClosingSpeed)
            {
                if (float.IsNaN(_calmSince))
                {
                    _calmSince = now;
                }
                else if (now - _calmSince >= ReleaseCalmSeconds)
                {
                    if (threat != null && threat != _threat && Retarget(mission, formation, terrain, centre, now, threat, gap, closing, "threatCalm", out what, out why))
                    {
                        return true;
                    }
                    Drop(now, "threatGone gap=" + GapText(_threat, trackedGap) + " calm=" + Fmt0(now - _calmSince) + "s", out what, out why);
                    return false;
                }
            }
            else
            {
                _calmSince = float.NaN;
            }
            if (threat != null && threat != _threat && gap < trackedGap - RetargetMargin && now - _changedAt >= MinChangeSeconds)
            {
                if (Retarget(mission, formation, terrain, centre, now, threat, gap, closing, "nearer", out what, out why))
                {
                    return true;
                }
            }

            // cover infantry gone / retreating / too small: another cover, else a fallback, else release
            if (_kind != Kind.Fallback && !CoverUsable(_cover, formation, false, _supportMain))
            {
                Candidate best;
                string refusal;
                if ((Pick(mission, formation, terrain, centre, _threat, false, out best, out refusal) || Fallback(mission, formation, terrain, centre, _threat, false, out best, ref refusal))
                    && Apply(formation, best, now))
                {
                    _changedAt = now;
                    _engaged = false;
                    _steps = (best.Kind == Kind.Fallback) ? 1 : 0;
                    what = "cover change";
                    why = "coverGone " + Describe(best, _threat, trackedGap, trackedClosing);
                    return true;
                }
                Drop(now, "coverGone " + TerrainCavalry.FormationName(_cover), out what, out why);
                return false;
            }

            bool arrived = SupportFire.ArrivedAt(formation, centre, _move.Desired, _facing, ArriveDistance);
            if (_kind == Kind.Fallback)
            {
                if (now - _builtAt >= RebuildMinSeconds && now - _changedAt >= MinChangeSeconds)
                {
                    Candidate best;
                    string refusal;
                    // cover may have become usable: prefer it over the fallback
                    if (Pick(mission, formation, terrain, centre, _threat, false, out best, out refusal) && Apply(formation, best, now))
                    {
                        _changedAt = now;
                        _steps = 0;
                        what = "cover change";
                        why = Describe(best, _threat, trackedGap, trackedClosing);
                        return true;
                    }
                    // arrived at the step: threat still pressing = another step (up to FallbackMaxSteps), else the cover ends
                    bool pressing = Pressing(trackedGap, trackedClosing);
                    if (arrived && pressing && _steps < FallbackMaxSteps)
                    {
                        refusal = null;
                        if (Fallback(mission, formation, terrain, centre, _threat, false, out best, ref refusal) && Apply(formation, best, now))
                        {
                            _changedAt = now;
                            _steps++;
                            what = "cover step";
                            why = Describe(best, _threat, trackedGap, trackedClosing) + " step=" + _steps.ToString(CultureInfo.InvariantCulture);
                            return true;
                        }
                    }
                    else if (arrived && !pressing)
                    {
                        Drop(now, "stepDone gap=" + GapText(_threat, trackedGap) + " closing=" + Fmt1(trackedClosing) + "m/s steps=" + _steps.ToString(CultureInfo.InvariantCulture), out what, out why);
                        return false;
                    }
                    _builtAt = now;
                }
                return true;
            }

            // engaged: the threat fights the cover infantry and the archers stand behind it - they shoot from there
            float threatCoverGap = FormationGeometry.Gap(_threat, FootprintMode.ScatterAware, _cover, FootprintMode.ScatterAware);
            if (!_engaged && arrived && threatCoverGap <= EngagedGap && RBMAI.Utilities.FormationFightingInMelee(_cover, EngagedMeleeRatio))
            {
                _engaged = true;
                what = "cover engaged";
                why = TerrainCavalry.FormationName(_cover) + " " + KindNames[(int)_kind] + " threat=" + TerrainCavalry.FormationName(_threat)
                    + " threatToCover=" + ThreatCoverText(threatCoverGap) + " held=" + Fmt0(now - _since) + "s";
            }
            else if (_engaged && threatCoverGap > EngagedGap * 2f)
            {
                _engaged = false;
                what = "cover disengaged";
                why = TerrainCavalry.FormationName(_cover) + " threat=" + TerrainCavalry.FormationName(_threat) + " threatToCover=" + ThreatCoverText(threatCoverGap);
            }

            if (now - _builtAt >= RebuildMinSeconds)
            {
                _builtAt = now;
                Vec2 front;
                Vec2 f;
                // against the frozen threat position: moves with the cover infantry, with the threat only once its bearing turned past FreezeTurnCos
                if (Place(formation, _cover, _kind, FrozenThreatCentre(trackedCentre), out front, out f))
                {
                    float limit = _engaged ? EngagedRebuildDistance : RebuildDistance;
                    if (front.DistanceSquared(_move.Desired) > limit * limit)
                    {
                        // same cover and side, moved with the infantry / threat (held point: KeepMaxShare)
                        string fail = Validate(mission, formation, terrain, centre, front, f, _threat, trackedCentre, _cover, true, EdgeMin, FriendlyOverlap.KeepMaxShare, true);
                        if (fail == null)
                        {
                            Candidate same = new Candidate { Cover = _cover, Kind = _kind, Front = front, Facing = f, Travel = centre.Distance(front) };
                            Apply(formation, same, now);
                        }
                        else if (now - _changedAt >= MinChangeSeconds && what == null)
                        {
                            Candidate best;
                            string refusal;
                            Formation oldCover = _cover;
                            Kind oldKind = _kind;
                            if (Pick(mission, formation, terrain, centre, _threat, false, out best, out refusal) && Apply(formation, best, now))
                            {
                                _changedAt = now;
                                _engaged = false;
                                if (best.Cover != oldCover || best.Kind != oldKind)
                                {
                                    what = "cover change";
                                    why = fail + " " + Describe(best, _threat, trackedGap, trackedClosing);
                                }
                            }
                        }
                    }
                }
            }
            return true;
        }

        /// <summary>Logged "cover change"; false when no point fits.</summary>
        private bool Retarget(Mission mission, Formation formation, BattleTerrain terrain, Vec2 centre, float now, Formation threat, float gap, float closing,
            string reason, out string what, out string why)
        {
            what = null;
            why = null;
            Candidate best;
            string refusal;
            if (!(Pick(mission, formation, terrain, centre, threat, false, out best, out refusal) || Fallback(mission, formation, terrain, centre, threat, false, out best, ref refusal))
                || !Apply(formation, best, now))
            {
                return false;
            }
            string old = TerrainCavalry.FormationName(_threat);
            _threat = threat;
            SetThreatRef(threat);
            _changedAt = now;
            _calmSince = float.NaN;
            _engaged = false;
            _steps = (best.Kind == Kind.Fallback) ? 1 : 0;
            what = "cover change";
            why = reason + " was=" + old + " " + Describe(best, threat, gap, closing);
            return true;
        }

        private void Drop(float now, string reason, out string what, out string why)
        {
            what = "cover release";
            why = reason + " held=" + Fmt0(now - _since) + "s";
            if (_active && _threat != null)
            {
                _releasedThreat = _threat;
                _releasedAt = now;
            }
            _active = false;
            _cover = null;
            _threat = null;
            _threatRef = Vec2.Invalid;
            _engaged = false;
            _steps = 0;
            _calmSince = float.NaN;
            _changedAt = now;
            _move.Clear();
            _facing = Vec2.Invalid;
        }

        private void SetThreatRef(Formation threat)
        {
            _threatRef = (threat != null) ? RBMAI.Utilities.GetFormationCenter(threat) : Vec2.Invalid;
        }

        /// <summary>The frozen threat position while its bearing from the cover infantry stays within FreezeTurnCos, else the tracked one (which becomes the new frozen one).</summary>
        private Vec2 FrozenThreatCentre(Vec2 tracked)
        {
            if (!tracked.IsValid)
            {
                return _threatRef;
            }
            Vec2 coverCentre = (_cover != null) ? RBMAI.Utilities.GetFormationCenter(_cover) : Vec2.Invalid;
            if (_threatRef.IsValid && coverCentre.IsValid)
            {
                Vec2 bearingNow = tracked - coverCentre;
                Vec2 bearingThen = _threatRef - coverCentre;
                if (bearingNow.LengthSquared > 1f && bearingThen.LengthSquared > 1f
                    && Vec2.DotProduct(bearingNow.Normalized(), bearingThen.Normalized()) >= FreezeTurnCos)
                {
                    return _threatRef;
                }
            }
            _threatRef = tracked;
            return tracked;
        }

        private bool Current(out WorldPosition target, out Vec2 facing)
        {
            target = _move.Point;
            facing = _facing;
            return _active && _move.Desired.IsValid && _facing.IsValid;
        }

        /// <summary>Sets the candidate as the target: a copy of a cached WorldPosition with ONE SetVec2MT, kept while the point stays put.</summary>
        private bool Apply(Formation formation, Candidate c, float now)
        {
            if (!c.Front.IsValid || !c.Facing.IsValid)
            {
                return false;
            }
            WorldPosition source = (c.Cover != null) ? c.Cover.CachedMedianPosition : formation.CachedMedianPosition;
            if (!source.AsVec2.IsValid)
            {
                return false;
            }
            // kept while it stays put (native caches the order position by its Vec2); a new cover always rebuilds
            WorldPosition target;
            bool rebuilt;
            _move.TryGet(c.Front, now, source, !_active || !_move.Desired.IsValid, ApplyKeepDistance, 0f, out target, out rebuilt);
            _facing = c.Facing;
            _cover = c.Cover;
            _kind = c.Kind;
            _builtAt = now;
            return true;
        }


        /// <summary>The nearest qualifying threat, its gap and closing speed; nearest = the nearest significant enemy melee formation in range (gap-shrink probe).</summary>
        private Formation FindThreat(Mission mission, Formation formation, Vec2 centre, float ownRadius, float now, out float threatGap, out float threatClosing, out Formation nearest)
        {
            threatGap = float.MaxValue;
            threatClosing = 0f;
            nearest = null;
            float nearestGap = float.MaxValue;
            Formation best = null;
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    // stand vs mounted: melee cavalry is no threat to take cover from
                    if (enemy == null || enemy.CountOfUnits <= 0 || !IsMeleeThreat(enemy) || enemy.QuerySystem.IsCavalryFormation
                        || !ThreatSignificance.IsSignificant(formation, enemy))
                    {
                        continue;
                    }
                    float gap;
                    float closing;
                    Vec2 c;
                    if (!Measure(enemy, centre, ownRadius, now, out gap, out closing, out c))
                    {
                        continue;
                    }
                    if (gap > InfantryThreatRange)
                    {
                        continue;
                    }
                    if (gap < nearestGap)
                    {
                        nearestGap = gap;
                        nearest = enemy;
                    }
                    if ((gap <= CloseRange || closing >= ClosingSpeed) && gap < threatGap)
                    {
                        threatGap = gap;
                        threatClosing = closing;
                        best = enemy;
                    }
                }
            }
            return best;
        }

        /// <summary>Stand vs mounted (user rule): the nearest significant, not retreating enemy cavalry / HA formation within CavalryNearGap, or CavalryThreatRange and closing at CavalryClosingSpeed, is only logged once per formation. Both sides, cached data.</summary>
        private void StandVsMounted(Mission mission, Formation formation, Vec2 centre, float ownRadius, float now, out string what, out string why)
        {
            what = null;
            why = null;
            if (!RBMConfig.RBMConfig.battleTerrainLoggingEnabled)
            {
                return;
            }
            Formation best = null;
            float bestGap = float.MaxValue;
            float bestClosing = 0f;
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (enemy == null || enemy.CountOfUnits <= 0 || enemy.GetReadonlyMovementOrderReference().OrderType == OrderType.Retreat
                        || !(enemy.QuerySystem.IsCavalryFormation || enemy.QuerySystem.IsRangedCavalryFormation)
                        || !ThreatSignificance.IsSignificant(formation, enemy))
                    {
                        continue;
                    }
                    float gap;
                    float closing;
                    Vec2 c;
                    if (!Measure(enemy, centre, ownRadius, now, out gap, out closing, out c) || gap > CavalryThreatRange)
                    {
                        continue;
                    }
                    if ((gap <= CavalryNearGap || closing >= CavalryClosingSpeed) && gap < bestGap)
                    {
                        best = enemy;
                        bestGap = gap;
                        bestClosing = closing;
                    }
                }
            }
            if (best == null || best == _standThreat)
            {
                return;
            }
            _standThreat = best;
            what = "stand vsMounted";
            why = ThreatSignificance.Describe(formation, best) + " gap=" + GapText(best, bestGap) + " closing=" + Fmt1(bestClosing) + "m/s melee="
                + (RBMAI.Utilities.FormationFightingInMelee(formation, EngagedMeleeRatio) ? "1" : "0");
        }

        private static bool Pressing(float gap, float closing)
        {
            return gap <= CloseRange || (gap <= InfantryThreatRange && closing >= ClosingSpeed);
        }

        /// <summary>Enemy foot that is not mostly ranged, or melee cavalry (not horse archers); not retreating.</summary>
        private static bool IsMeleeThreat(Formation enemy)
        {
            if (enemy.GetReadonlyMovementOrderReference().OrderType == OrderType.Retreat)
            {
                return false;
            }
            FormationQuerySystem q = enemy.QuerySystem;
            if (q.IsRangedCavalryFormation)
            {
                return false;
            }
            if (q.IsCavalryFormation)
            {
                return true;
            }
            return !RBMAI.Utilities.IsRangedForArrangement(enemy);
        }

        /// <summary>Edge gap to the archers and closing speed: sampled velocity (EnemyMotion) toward them or the gap's shrink rate, whichever is larger.</summary>
        private bool Measure(Formation enemy, Vec2 centre, float ownRadius, float now, out float gap, out float closing, out Vec2 enemyCentre)
        {
            gap = float.MaxValue;
            closing = 0f;
            enemyCentre = Vec2.Invalid;
            if (enemy == null || enemy.CountOfUnits <= 0)
            {
                return false;
            }
            Vec2 c = RBMAI.Utilities.GetFormationCenter(enemy);
            if (!c.IsValid)
            {
                return false;
            }
            Vec2 toUs = centre - c;
            float distance = toUs.Normalize();
            Footprint shape;
            gap = (_ownValid && FormationGeometry.TryGet(enemy, FootprintMode.ScatterAware, out shape))
                ? FormationGeometry.Gap(_own, shape)
                : distance - MathF.Max(enemy.Width, enemy.Depth) * 0.5f - ownRadius;
            Vec2 velocity;
            if (EnemyMotion.TryVelocity(enemy, out velocity) && distance > 0.01f)
            {
                closing = velocity.DotProduct(toUs);
            }
            if (enemy == _probe)
            {
                float dt = now - _probeAt;
                if (dt >= ProbeMinSeconds && dt <= ProbeMaxSeconds)
                {
                    // centre distance: a Line <-> Loose change is no closing speed
                    closing = MathF.Max(closing, (_probeDistance - distance) / dt);
                }
            }
            enemyCentre = c;
            return true;
        }


        /// <summary>Friendly melee infantry big enough to screen the archers, not retreating and not charging (PostUncovered passes allowCharging to report it); the engaged main they support counts under Charge too (geometry still needs the archers behind it).</summary>
        private static bool CoverUsable(Formation friend, Formation formation, bool allowCharging = false, Formation engagedMain = null)
        {
            if (friend != null && friend == engagedMain)
            {
                allowCharging = true;
            }
            if (friend == null || friend == formation || formation == null || friend.Team == null || formation.Team == null || friend.Team.Side != formation.Team.Side)
            {
                return false;
            }
            int men = friend.CountOfUnits;
            if (men < CoverMinMen || men < (int)System.Math.Ceiling(CoverMenShare * formation.CountOfUnits))
            {
                return false;
            }
            OrderType order = friend.GetReadonlyMovementOrderReference().OrderType;
            // ChargeGuard.IsChargeIntent: a line walking to its charge leaves the archers just the same
            if (order == OrderType.Retreat || (!allowCharging && ChargeGuard.IsChargeIntent(friend)))
            {
                return false;
            }
            FormationQuerySystem q = friend.QuerySystem;
            return !q.IsCavalryFormation && !q.IsRangedCavalryFormation && !RBMAI.Utilities.IsRangedForArrangement(friend);
        }

        private static bool Covered(Mission mission, Formation formation, Vec2 centre, float ownRadius, Formation threat, Formation engagedMain, out Formation screen)
        {
            screen = null;
            Vec2 threatCentre = RBMAI.Utilities.GetFormationCenter(threat);
            if (!threatCentre.IsValid)
            {
                return false;
            }
            Vec2 v = centre - threatCentre;
            float length = v.Normalize();
            if (length < 1f)
            {
                return false;
            }
            Vec2 across = v.LeftVec();
            Footprint own;
            bool ownValid = FormationGeometry.TryGet(formation, FootprintMode.StableRanged, out own);
            BattleSideEnum side = formation.Team.Side;
            foreach (Team team in mission.Teams)
            {
                if (team == null || team.Side != side)
                {
                    continue;
                }
                foreach (Formation friend in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (!CoverUsable(friend, formation, false, engagedMain))
                    {
                        continue;
                    }
                    Vec2 c = RBMAI.Utilities.GetFormationCenter(friend);
                    if (!c.IsValid)
                    {
                        continue;
                    }
                    Vec2 rel = c - threatCentre;
                    float along = Vec2.DotProduct(rel, v);
                    if (along < 0f || along > length + ExtentAlong(friend, v))
                    {
                        continue;
                    }
                    if (MathF.Abs(Vec2.DotProduct(rel, across)) > ExtentAlong(friend, across) + CoveredSlack)
                    {
                        continue;
                    }
                    if (CoverGap(friend, c, centre, ownRadius, ownValid, own) > CoveredMaxGap)
                    {
                        continue;
                    }
                    screen = friend;
                    return true;
                }
            }
            return false;
        }

        /// <summary>The archers stand at a post next to friendly cover infantry ("behind fN:Class" / "beside fN:Class", within PostMaxGap), or null. Cached formation data only.</summary>
        private static string AtPost(Mission mission, Formation formation, Vec2 centre, Formation engagedMain)
        {
            float ownHalfWidth = RBMAI.Utilities.RangedStableRadius(formation);
            float ownHalfDepth = MathF.Max(formation.Depth, FriendlyOverlap.MinDepth) * 0.5f;
            BattleSideEnum side = formation.Team.Side;
            foreach (Team team in mission.Teams)
            {
                if (team == null || team.Side != side)
                {
                    continue;
                }
                foreach (Formation friend in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (!CoverUsable(friend, formation, false, engagedMain))
                    {
                        continue;
                    }
                    Vec2 c = RBMAI.Utilities.GetFormationCenter(friend);
                    Vec2 dir = friend.Direction;
                    if (!c.IsValid || !dir.IsValid || dir.LengthSquared < 0.0001f)
                    {
                        continue;
                    }
                    dir = dir.Normalized();
                    Vec2 rel = centre - c;
                    float along = Vec2.DotProduct(rel, dir);
                    float across = MathF.Abs(Vec2.DotProduct(rel, dir.RightVec()));
                    float halfWidth = MathF.Max(friend.Width, 1f) * 0.5f;
                    float halfDepth = MathF.Max(friend.Depth, 1f) * 0.5f;
                    // edge gap behind its rear plane (negative = overlapping) and beyond its end
                    float behindGap = -along - halfDepth - ownHalfDepth;
                    float besideGap = across - halfWidth - ownHalfWidth;
                    if (behindGap <= PostMaxGap && along <= 0f && across <= halfWidth + ownHalfWidth)
                    {
                        return "behind " + TerrainCavalry.FormationName(friend) + " gap=" + Fmt0(MathF.Max(0f, behindGap)) + "m";
                    }
                    if (besideGap <= PostMaxGap && besideGap >= -ownHalfWidth && along <= halfDepth + ownHalfDepth && along >= -(halfDepth + ownHalfDepth + PostMaxGap))
                    {
                        return "beside " + TerrainCavalry.FormationName(friend) + " gap=" + Fmt0(MathF.Max(0f, besideGap)) + "m";
                    }
                }
            }
            return null;
        }

        /// <summary>A post stands covered only while friendly cover infantry is next to it: not charging, not walking away (> PostLeaveSpeed), within PostCoverGap, threat in FRONT of it. Null when covered, else why not. Cached formation data only.</summary>
        private static string PostUncovered(Mission mission, Formation formation, Vec2 centre, float ownRadius, Formation threat, Formation engagedMain)
        {
            Vec2 threatCentre = RBMAI.Utilities.GetFormationCenter(threat);
            if (!threatCentre.IsValid)
            {
                return null;
            }
            string first = null;
            Footprint own;
            bool ownValid = FormationGeometry.TryGet(formation, FootprintMode.StableRanged, out own);
            BattleSideEnum side = formation.Team.Side;
            foreach (Team team in mission.Teams)
            {
                if (team == null || team.Side != side)
                {
                    continue;
                }
                foreach (Formation friend in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (!CoverUsable(friend, formation, true))
                    {
                        continue;
                    }
                    Vec2 c = RBMAI.Utilities.GetFormationCenter(friend);
                    if (!c.IsValid)
                    {
                        continue;
                    }
                    float edge = CoverGap(friend, c, centre, ownRadius, ownValid, own);
                    if (edge > PostCoverGap)
                    {
                        continue;
                    }
                    string name = TerrainCavalry.FormationName(friend);
                    // the engaged main they support fights in front of them: its Charge is no reason; an advance to a charge counts as charging
                    if (ChargeGuard.IsChargeIntent(friend) && friend != engagedMain)
                    {
                        first = first ?? name + " charging";
                        continue;
                    }
                    Vec2 away = c - centre;
                    float awayLength = away.Normalize();
                    Vec2 velocity = friend.CachedCurrentVelocity;
                    float leaving = (velocity.IsValid && awayLength > 0.01f) ? Vec2.DotProduct(velocity, away) : 0f;
                    if (leaving > PostLeaveSpeed)
                    {
                        first = first ?? name + " leaving " + Fmt1(leaving) + "m/s";
                        continue;
                    }
                    Vec2 dir = friend.Direction;
                    if (!dir.IsValid || dir.LengthSquared < 0.0001f || Vec2.DotProduct(threatCentre - c, dir.Normalized()) <= 0f)
                    {
                        first = first ?? name + " threat behind it";
                        continue;
                    }
                    // the archers must stand behind its front plane
                    float ahead = Vec2.DotProduct(centre - c, dir.Normalized()) - MathF.Max(friend.Depth, 1f) * 0.5f;
                    if (ahead > 0f)
                    {
                        first = first ?? name + " archers " + Fmt0(ahead) + "m ahead of it";
                        continue;
                    }
                    return null;
                }
            }
            return first ?? "no line within " + Fmt0(PostCoverGap) + "m";
        }

        /// <summary>refusal = why none (only when explain).</summary>
        private bool Pick(Mission mission, Formation formation, BattleTerrain terrain, Vec2 centre, Formation threat, bool explain, out Candidate best, out string refusal)
        {
            best = default(Candidate);
            refusal = null;
            Vec2 threatCentre = RBMAI.Utilities.GetFormationCenter(threat);
            if (!threatCentre.IsValid)
            {
                refusal = "noThreatCentre";
                return false;
            }
            StringBuilder sb = null;
            float bestScore = float.MaxValue;
            bool found = false;
            BattleSideEnum side = formation.Team.Side;
            foreach (Team team in mission.Teams)
            {
                if (team == null || team.Side != side)
                {
                    continue;
                }
                foreach (Formation friend in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (!CoverUsable(friend, formation, false, _supportMain))
                    {
                        continue;
                    }
                    for (int k = (int)Kind.Behind; k <= (int)Kind.Beside; k++)
                    {
                        Kind kind = (Kind)k;
                        Vec2 front;
                        Vec2 f;
                        if (!Place(formation, friend, kind, threatCentre, out front, out f))
                        {
                            continue;
                        }
                        string fail = Validate(mission, formation, terrain, centre, front, f, threat, threatCentre, friend, true, EdgeMin, FriendlyOverlap.TakeMaxShare, explain);
                        if (fail == null)
                        {
                            // worth the walk only when clearly farther from the threat
                            float footprint = MathF.Max(formation.Depth, FriendlyOverlap.MinDepth) * 0.5f;
                            if ((front - f * footprint).Distance(threatCentre) < centre.Distance(threatCentre) + CoverMinGain)
                            {
                                fail = "noGain";
                            }
                        }
                        if (fail == null)
                        {
                            float depth = MathF.Max(formation.Depth, FriendlyOverlap.MinDepth);
                            float travel = centre.Distance(front - f * (depth * 0.5f));
                            float score = travel + KindPenalty[k];
                            if (score < bestScore)
                            {
                                bestScore = score;
                                best = new Candidate { Cover = friend, Kind = kind, Front = front, Facing = f, Travel = travel };
                                found = true;
                            }
                        }
                        else if (explain)
                        {
                            sb = (sb == null) ? new StringBuilder() : sb.Append(',');
                            sb.Append(TerrainCavalry.FormationName(friend)).Append('/').Append(KindNames[k]).Append(':').Append(fail);
                        }
                    }
                }
            }
            if (!found && explain)
            {
                refusal = (sb == null) ? "noInfantry" : sb.ToString();
            }
            return found;
        }

        /// <summary>A candidate's front-centre and facing (toward the threat) for a kind next to the cover; false without usable geometry.</summary>
        private static bool Place(Formation formation, Formation cover, Kind kind, Vec2 threatCentre, out Vec2 front, out Vec2 facing)
        {
            front = Vec2.Invalid;
            facing = Vec2.Invalid;
            Vec2 coverCentre = RBMAI.Utilities.GetFormationCenter(cover);
            if (cover == null || !coverCentre.IsValid || !threatCentre.IsValid)
            {
                return false;
            }
            Vec2 u = coverCentre - threatCentre;
            if (u.LengthSquared < 1f)
            {
                return false;
            }
            u = u.Normalized();
            float depth = MathF.Max(formation.Depth, FriendlyOverlap.MinDepth);
            float width = MathF.Max(formation.Width, FriendlyOverlap.MinWidth);
            if (kind == Kind.Beside)
            {
                Vec2 dir = cover.Direction;
                if (!dir.IsValid || dir.LengthSquared < 0.0001f)
                {
                    return false;
                }
                dir = dir.Normalized();
                Vec2 right = dir.RightVec();
                Vec2 away = (Vec2.DotProduct(threatCentre - coverCentre, right) > 0f) ? -right : right;
                Vec2 at = coverCentre + away * (MathF.Max(cover.Width, 1f) * 0.5f + BesideGap + width * 0.5f);
                Vec2 toThreat = threatCentre - at;
                if (toThreat.LengthSquared < 1f)
                {
                    return false;
                }
                facing = toThreat.Normalized();
                front = at + facing * (depth * 0.5f);
                return true;
            }
            // behind: archers face the threat (f = -u); front BehindGap behind the infantry's rear edge, ranks behind that (front-centre rule)
            facing = -u;
            front = coverCentre + u * (ExtentAlong(cover, u) + BehindGap);
            if (kind == Kind.BehindLeft || kind == Kind.BehindRight)
            {
                Vec2 left = facing.LeftVec();
                float shift = MathF.Max(5f, ExtentAlong(cover, left) * 0.5f);
                front = front + left * ((kind == Kind.BehindLeft) ? shift : -shift);
            }
            return true;
        }

        /// <summary>Fallback without cover: a FallbackStep away from the threat bent toward the team average (straight, then +-30 / +-60 deg).</summary>
        private bool Fallback(Mission mission, Formation formation, BattleTerrain terrain, Vec2 centre, Formation threat, bool explain, out Candidate best, ref string refusal)
        {
            best = default(Candidate);
            Vec2 threatCentre = RBMAI.Utilities.GetFormationCenter(threat);
            if (!threatCentre.IsValid)
            {
                return false;
            }
            Vec2 away = centre - threatCentre;
            if (away.LengthSquared < 1f)
            {
                return false;
            }
            away = away.Normalized();
            Vec2 dir = away;
            // SupportFire: a step bends toward the support place, not the team average
            Vec2 army = _refuge.IsValid ? _refuge : formation.QuerySystem.Team.AveragePosition;
            if (army.IsValid && army.DistanceSquared(centre) > 400f)
            {
                Vec2 toArmy = (army - centre).Normalized();
                if (Vec2.DotProduct(toArmy, away) > -0.5f)
                {
                    Vec2 blend = away + toArmy * 0.5f;
                    if (blend.LengthSquared > 0.01f)
                    {
                        dir = blend.Normalized();
                    }
                }
            }
            float depth = MathF.Max(formation.Depth, FriendlyOverlap.MinDepth);
            StringBuilder sb = null;
            for (int i = 0; i < 5; i++)
            {
                // 0, +30, -30, +60, -60 deg.
                float cos = (i == 0) ? 1f : (i <= 2) ? Cos30 : 0.5f;
                float sin = (i == 0) ? 0f : (i <= 2) ? 0.5f : Cos30;
                if (i == 2 || i == 4)
                {
                    sin = -sin;
                }
                Vec2 d = new Vec2(dir.x * cos - dir.y * sin, dir.x * sin + dir.y * cos);
                Vec2 at = centre + d * FallbackStep;
                Vec2 toThreat = threatCentre - at;
                if (toThreat.LengthSquared < 1f)
                {
                    continue;
                }
                Vec2 f = toThreat.Normalized();
                Vec2 front = at + f * (depth * 0.5f);
                string fail = Validate(mission, formation, terrain, centre, front, f, threat, threatCentre, null, false, FallbackEdgeMin, FriendlyOverlap.TakeMaxShare, explain);
                if (fail == null)
                {
                    best = new Candidate { Cover = null, Kind = Kind.Fallback, Front = front, Facing = f, Travel = FallbackStep };
                    return true;
                }
                if (explain)
                {
                    sb = (sb == null) ? new StringBuilder() : sb.Append(',');
                    sb.Append(i).Append(':').Append(fail);
                }
            }
            if (explain && sb != null)
            {
                refusal = ((refusal != null) ? refusal + " " : "") + "fallback=" + sb.ToString();
            }
            return false;
        }

        /// <summary>Why the archers cannot stand with their front here, or null. coverKind = a cover candidate, else a fallback. explain = false returns bare words (callers that only test for null: no string formatting).</summary>
        private static string Validate(Mission mission, Formation formation, BattleTerrain terrain, Vec2 from, Vec2 front, Vec2 f, Formation threat, Vec2 threatCentre,
            Formation cover, bool coverKind, float edgeMin, float maxShare, bool explain)
        {
            float depth = MathF.Max(formation.Depth, FriendlyOverlap.MinDepth);
            Vec2 at = front - f * (depth * 0.5f);
            if (from.Distance(at) > CoverMaxTravel)
            {
                return "far";
            }
            string ground = SupportFire.GroundFail(terrain, formation, front, f, edgeMin, false);
            if (ground != null)
            {
                return ground;
            }
            float threatRadius = MathF.Max(threat.Width, threat.Depth) * 0.5f;
            float atThreat = at.Distance(threatCentre);
            if (coverKind)
            {
                Vec2 coverCentre = RBMAI.Utilities.GetFormationCenter(cover);
                if (coverCentre.IsValid && atThreat < coverCentre.Distance(threatCentre))
                {
                    return "toward";
                }
            }
            else if (atThreat <= from.Distance(threatCentre))
            {
                return "toward";
            }
            float startDistance = from.Distance(threatCentre);
            if (SegmentDistance(from, at, threatCentre) < MathF.Min(threatRadius + PathClear, startDistance) - 1f)
            {
                return "path";
            }
            if (!coverKind)
            {
                // user rule (FormationCohesion): a fallback step never goes beyond the limit from the main infantry nor farther than they stand
                Formation anchor;
                float gapThere;
                float limit;
                if (!FormationCohesion.Within(formation, front, f, FormationCohesion.Kind.Auto, null, 0f, out anchor, out gapThere, out limit)
                    && !(gapThere <= FormationCohesion.CurrentGap(formation, anchor)))
                {
                    return explain ? FormationCohesion.RefusalText(anchor, gapThere, limit) : "cohesion";
                }
            }
            Formation friend;
            float share = FriendlyOverlap.Share(formation, front, f, null, FriendlyOverlap.Mode.HeadingOnly, out friend);
            if (share > maxShare)
            {
                return explain ? "overlap(" + TerrainCavalry.FormationName(friend) + ")" : "overlap";
            }
            Formation other = OtherEnemyNear(mission, formation, threat, from, at);
            if (other != null)
            {
                return explain ? "enemy(" + TerrainCavalry.FormationName(other) + ")" : "enemy";
            }
            return null;
        }

        /// <summary>A significant enemy melee formation other than the threat within OtherEnemyMinGap (edge) of <paramref name="at"/> and nearer to it than to <paramref name="from"/>.</summary>
        private static Formation OtherEnemyNear(Mission mission, Formation formation, Formation threat, Vec2 from, Vec2 at)
        {
            float ownRadius = RBMAI.Utilities.RangedStableRadius(formation);
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (enemy == null || enemy == threat || enemy.CountOfUnits <= 0 || !IsMeleeThreat(enemy) || !ThreatSignificance.IsSignificant(formation, enemy))
                    {
                        continue;
                    }
                    Footprint shape;
                    if (!FormationGeometry.TryGet(enemy, FootprintMode.ScatterAware, out shape))
                    {
                        continue;
                    }
                    // own block as its stable radius round the candidate point (its facing there is the caller's)
                    float gapThere = FormationGeometry.GapToPoint(shape, at) - ownRadius;
                    if (gapThere < OtherEnemyMinGap && gapThere < FormationGeometry.GapToPoint(shape, from) - ownRadius)
                    {
                        return enemy;
                    }
                }
            }
            return null;
        }


        private static float ExtentAlong(Formation formation, Vec2 u)
        {
            Footprint shape;
            return FormationGeometry.TryGet(formation, FootprintMode.Current, out shape) ? FormationGeometry.ExtentAlong(shape, u) : 0f;
        }

        /// <summary>Edge gap from the cover infantry to the archers' own stable block (circle rule without one).</summary>
        private static float CoverGap(Formation friend, Vec2 friendCentre, Vec2 centre, float ownRadius, bool ownValid, in Footprint own)
        {
            Footprint shape;
            if (ownValid && FormationGeometry.TryGet(friend, FootprintMode.ScatterAware, out shape))
            {
                return FormationGeometry.Gap(own, shape);
            }
            return friendCentre.Distance(centre) - MathF.Max(friend.Width, friend.Depth) * 0.5f - ownRadius;
        }

        private string ThreatCoverText(float gap)
        {
            return FormationGeometry.GapText(gap);
        }

        private static float SegmentDistance(Vec2 a, Vec2 b, Vec2 p)
        {
            Vec2 ab = b - a;
            float length2 = ab.LengthSquared;
            float t = (length2 < 0.0001f) ? 0f : MBMath.ClampFloat(Vec2.DotProduct(p - a, ab) / length2, 0f, 1f);
            return (a + ab * t).Distance(p);
        }

        private string Describe(Candidate c, Formation threat, float gap, float closing)
        {
            return ((c.Cover != null) ? TerrainCavalry.FormationName(c.Cover) + " " : "") + KindNames[(int)c.Kind] + " threat=" + TerrainCavalry.FormationName(threat)
                + " gap=" + GapText(threat, gap) + " closing=" + Fmt1(closing) + "m/s d=" + Fmt0(c.Travel) + "m";
        }

        private static string GapText(Formation enemy, float gap)
        {
            return FormationGeometry.GapText(gap);
        }

        private static string Fmt0(float value)
        {
            return TerrainBattlePosition.Fmt0(value);
        }

        private static string Fmt1(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
