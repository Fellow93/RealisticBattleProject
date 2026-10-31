using System.Globalization;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// SUPPORT FIRE (user rule): once the frontlines connect, ranged formations find at least a suitable position and fire; with vision at a safe
    /// distance they stand and shoot, without vision they search for a safe terrain position or rotate. One instance per behavior, used by
    /// ArcherSkirmish, ArcherFlank and the HoldPosition / HoldChoke Archers roles (not the hedgehog's). The behavior calls Evaluate after its
    /// ArcherCover check; while it returns true the archers Move to the support target facing the given way instead of their own order.
    ///
    /// WHO: AI foot ranged formations (IsRangedArrangementSubject), both sides, only while the behavior is active.
    ///
    /// CONNECTED: the team's main infantry (AI- or player-led) is >= ConnectMeleeRatio in melee with a significant enemy formation within
    /// MeleeProbeGap (the fronts), or under Charge / ChargeWithTarget with one within ConnectChargeGap. Read at most every ConnectCheckSeconds,
    /// held ConnectHoldSeconds. ENGAGED ENEMY = the nearest such formation (kept unless gone or another is RetargetMargin nearer).
    /// MAIN = the behavior's position anchor (ArmyComposition.AnchorFor): the main infantry, else the foot anchor; beside an ARCHER anchor connected = the nearest
    /// significant enemy within TakeRange (KeepRange while supporting) x the reach of the anchor.
    /// ALONE (the formation is the anchor, or there is none): the archers are their own anchor ("main" = itself): connected = the nearest significant enemy within TakeRange
    /// (KeepRange while supporting) x the reach; "ahead" = past their own front, no cohesion test, no mainMoved / outOfRange re-pick (the connection ends it);
    /// behind / sides / search are laid out round the archers themselves. Exhausted = the last pick or the ladder ended in "none" (ArcherSkirmish's initiative).
    ///
    /// STAND AND SHOOT: the current place is kept when usable: not ahead of the main's centre on the main -> enemy axis, the engaged enemy within
    /// TakeRange x the reach (half max missile range + both half depths), no other significant enemy melee formation within SafeEnemyGap (the engaged
    /// one at least StandMinGap off), within the cohesion limit (+ KeepSlack), no friend overlap above KeepMaxShare, and vision (shooting now >= NoShootRatio
    /// or a clear BattleTerrain.SightClear). They do not walk to a "better" point just because it scores higher. Stand vs mounted (user rule): cavalry /
    /// horse archers never count in the safety gaps, and a mounted engaged enemy riding out of KeepRange starts no re-pick.
    ///
    /// SUPPORT PLACE (current place unusable): candidates by the front-centre rule (FRONT at the point): behind (BehindGap behind the main's rear plane),
    /// left / right (moved sideways by half the main's extent), besideLeft / besideRight (level with the main's centre, beyond its end), and an archer spot
    /// of the side within SpotRange of the behind point (positions only, no claim). Refused: unwalkable / wet / near the edge, ahead of the main, out of range,
    /// enemy too close, beyond the cohesion limit (main as anchor, also under Charge), friend overlap above TakeMaxShare. Score = walk + kind penalty - spot bonus
    /// - height gain x HeightBonus, + CavalrySidePenalty, + NoSightPenalty, -/+ PreferredSideBonus. No terrain snapshot = the behind point only. Nothing usable
    /// (Fallback): "behindRelaxed", else "standNoPlace", else "support none" / "support lost noPlace" (the behavior's own order; ArcherCover answers the threat) -
    /// never a walk in front of the line. A caller's post that serves again is returned to only RepickMinSeconds after the support pick.
    ///
    /// STICKY: re-pick only when the engaged enemy is beyond KeepRange x the reach for OutOfRangeSeconds, the main moved > MainMovedRepick, the main changed,
    /// friend overlap > KeepMaxShare, the ground check fails or the place is ahead of the main - at most once per RepickMinSeconds (the last three at once).
    ///
    /// VISION: arrived and GraceSeconds after an arrival / change, with ammunition (>= AmmoRatio) and the engaged enemy within KeepRange x the reach, a
    /// MakingRangedAttackRatio under NoShootRatio for NoVisionSeconds = no vision. Ladder, one step per spell, at most one move per RepickMinSeconds: ROTATE
    /// (face the EnemyConcentration when > RotateMinDegrees off), SHIFT (side candidates with a clear sight line), SEARCH (a SearchStep lattice round the behind
    /// point, SearchRadius across, SearchBack back: all rules plus sight, higher ground preferred), then "support none" and restart after LadderRestSeconds.
    ///
    /// FACING: toward the engaged enemy, or the caller's preferred target (counter-fire) when within KeepRange x the reach (it never moves the formation); turned at
    /// FacingRefresh rad off. After a ROTATE: the concentration's bearing until the next pick.
    ///
    /// COVER / COHESION: while supporting, the main is the cohesion anchor even under Charge (FormationCohesion.SetSupportAnchor), ArcherCover counts the engaged
    /// main as cover and bends its fallback steps toward the support place; the place is kept through a cover.
    ///
    /// Log: BEHAVIOR queue, point "support" (take|move|keep|rotate|shift|lost|none). Decision label: Label.
    ///
    /// THREADING: AI thread only: cached formation data and query-system ratios, the immutable terrain snapshot and published spot plan; the target is a copy of
    /// a cached WorldPosition with ONE SetVec2MT, rebuilt only when the place moves; no engine query, no WorldPosition construction, no MBRandom.
    /// Per-behavior instance fields (Label volatile for the main thread), no statics.
    /// </summary>
    internal sealed class SupportFire
    {

        // Gaps in m (fronts / edge gaps as named in the class comment), times in s.
        internal const float ConnectMeleeRatio = 0.1f;
        internal const float MeleeProbeGap = 60f;
        internal const float ConnectChargeGap = 30f;
        internal const float ConnectCheckSeconds = 1f; // the melee share iterates the main's men
        internal const float ConnectHoldSeconds = 8f;
        internal const float RetargetMargin = 10f; // another enemy this much nearer to the main becomes the engaged one
        internal const float BehindGap = 20f; // front behind the main's rear plane
        internal const float TakeRange = 0.9f; // x reach (TerrainSpots.SkirmishTakeRange)
        internal const float KeepRange = 1.1f; // x reach (TerrainSpots.SkirmishKeepRange)
        internal const float OutOfRangeSeconds = 5f;
        internal const float MainMovedRepick = 40f;
        internal const float RepickMinSeconds = 15f; // re-pick / ladder move
        internal const float StandMinGap = 25f;
        internal const float SafeEnemyGap = 60f;
        internal const float SpotRange = 60f; // archer spot from the behind point
        internal const float SearchStep = 10f; // SEARCH lattice: step, half width across the axis, depth behind
        internal const float SearchRadius = 80f;
        internal const float SearchBack = 60f;
        internal const float NoShootRatio = 0.05f;
        internal const float NoVisionSeconds = 8f;
        internal const float GraceSeconds = 10f; // after an arrival / change
        internal const float AmmoRatio = 0.3f;
        internal const float RotateMinDegrees = 25f;
        internal const float LadderRestSeconds = 30f; // after "support none"
        internal const float FacingRefresh = 0.5f; // rad, as ArcherSkirmish
        internal const float ArriveDistance = 15f; // + a quarter of the depth
        internal const float EdgeMin = 20f; // from the map edge
        internal const float CavalryNear = 150f; // enemy melee cavalry this close to the main makes its side worse

        // score terms (m of walk)
        private const float SideKindPenalty = 5f;
        private const float BesideKindPenalty = 20f;
        private const float SpotBonus = 15f;
        private const float HeightBonus = 2f;
        private const float SearchHeightBonus = 3f;
        private const float CavalrySidePenalty = 40f;
        private const float NoSightPenalty = 60f;
        private const float PreferredSideBonus = 15f;

        private enum Ladder
        {
            None,
            Rotated,
            Shifted,
            Searched
        }

        private struct Candidate
        {
            public string Kind;
            public Vec2 Front;
            public Vec2 Facing;
            public float Score;
            public float Travel;
            public TerrainSpots.Spot Spot;
        }

        private bool _active;
        private Formation _main;
        private Formation _enemy;
        private float _connectedAt = float.NegativeInfinity;
        private float _checkedAt = float.NegativeInfinity;
        private Formation _checkedMain;
        private bool _lastConnected;
        private string _lastHow;
        private const float ApplyKeepDistance = 0.5f; // m: Apply keeps the built point this close
        /// <summary>The support Move point (front-centre); Desired = its Vec2.</summary>
        private readonly CachedMovePoint _move = new CachedMovePoint();
        private Vec2 _facing = Vec2.Invalid;
        private string _kind;
        private Vec2 _mainAt = Vec2.Invalid;
        private float _pickedAt = float.NegativeInfinity;
        private float _arrivedAt = float.NaN;
        private float _graceUntil = float.NegativeInfinity;
        private float _outOfRangeSince = float.NaN;
        private float _noShootSince = float.NaN;
        private Ladder _ladder = Ladder.None;
        private float _ladderAt = float.NegativeInfinity;
        private bool _faceConcentration;
        private bool _keepLogged;
        private string _noneLogged;
        private volatile string _label;
        private bool _exhaustedFlag;
        private float _exhaustedAt = float.NegativeInfinity;

        // set: stamped; cleared: a new place, firing, a new engaged enemy, or ExhaustedSeconds after it was set
        private bool _exhausted
        {
            get { return _exhaustedFlag; }
            set
            {
                if (value && !_exhaustedFlag)
                {
                    _exhaustedAt = MBCommon.GetTotalMissionTime();
                }
                _exhaustedFlag = value;
            }
        }

        internal const float ExhaustedSeconds = 30f;

        /// <summary>AI thread.</summary>
        internal Formation Main => _active ? _main : null;

        /// <summary>AI thread: the last pick found no place, or the vision ladder ended in "none", within ExhaustedSeconds; cleared by the next place, firing or a new engaged enemy.</summary>
        internal bool Exhausted => _exhaustedFlag && MBCommon.GetTotalMissionTime() - _exhaustedAt < ExhaustedSeconds;

        /// <summary>Footprint centre (ArcherCover's fallback bends toward it); invalid when not supporting. AI thread.</summary>
        internal Vec2 Refuge(Formation formation)
        {
            if (!_active || !_move.Desired.IsValid || !_facing.IsValid || formation == null)
            {
                return Vec2.Invalid;
            }
            return _move.Desired - _facing * (MathF.Max(formation.Depth, FriendlyOverlap.MinDepth) * 0.5f);
        }

        /// <summary>Decision layer text ("support behind", ...); null when not supporting. Any thread.</summary>
        internal string Label => _label;

        /// <summary>AI thread, after ArcherCover. True = support target (front-centre, cached) and facing; false = the behavior's own order. what / why = a log event. preferTarget: faced instead of the engaged enemy when in reach. post / postFacing = the caller's fixed place (invalid: none), kept (false) while usable. preferredSide: side candidates.</summary>
        internal bool Evaluate(Formation formation, bool active, Formation preferTarget, Vec2 post, Vec2 postFacing, FormationAI.BehaviorSide preferredSide,
            out WorldPosition target, out Vec2 facing, out string what, out string why)
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
                if (formation.Team == null || !RBMAI.Utilities.IsRangedArrangementSubject(formation))
                {
                    if (_active)
                    {
                        Drop(formation, now, "notRanged", out what, out why);
                    }
                    return false;
                }
                // the behavior's own anchor (a second archer formation supports beside the archer anchor); the anchor itself acts alone
                Formation main = ArmyComposition.AnchorFor(formation) ?? formation;
                Formation enemy;
                string how;
                if (!Connection(mission, formation, main, now, out enemy, out how))
                {
                    if (_active)
                    {
                        Drop(formation, now, "notConnected " + how, out what, out why);
                    }
                    return false;
                }
                Vec2 centre = RBMAI.Utilities.GetFormationCenter(formation);
                Vec2 mainCentre = RBMAI.Utilities.GetFormationCenter(main);
                Vec2 enemyCentre = RBMAI.Utilities.GetFormationCenter(enemy);
                if (!centre.IsValid || !mainCentre.IsValid || !enemyCentre.IsValid)
                {
                    return _active && Current(out target, out facing);
                }
                Vec2 axis = enemyCentre - mainCentre;
                if (axis.LengthSquared < 1f)
                {
                    axis = main.Direction;
                }
                if (!axis.IsValid || axis.LengthSquared < 0.0001f)
                {
                    return _active && Current(out target, out facing);
                }
                axis = axis.Normalized();
                BattleTerrain terrain = BattleTerrain.Current;
                Geometry g = new Geometry(formation, main, enemy, mainCentre, enemyCentre, axis, Reach(formation, enemy));

                // the caller's fixed post is kept while it is a usable support place (back to it only RepickMinSeconds after the pick: no flip-flop on the range edge)
                if (post.IsValid && (!_active || now - _pickedAt >= RepickMinSeconds))
                {
                    Vec2 pf = postFacing.IsValid && postFacing.LengthSquared > 0.0001f ? postFacing.Normalized() : axis;
                    if (PlaceFail(mission, formation, terrain, g, post, pf, false, FriendlyOverlap.KeepMaxShare) == null)
                    {
                        if (_active)
                        {
                            Drop(formation, now, "postOk", out what, out why);
                        }
                        return false;
                    }
                }

                if (!_active)
                {
                    Take(mission, formation, terrain, g, centre, main, enemy, now, preferredSide, out what, out why);
                    return _active && Current(out target, out facing);
                }

                // held: the sticky place
                if (main != _main)
                {
                    _main = main;
                    FormationCohesion.SetSupportAnchor(formation, main);
                }
                float depth = MathF.Max(formation.Depth, FriendlyOverlap.MinDepth);
                Vec2 placeCentre = _move.Desired - _facing * (depth * 0.5f);
                float toEnemy = placeCentre.Distance(enemyCentre);
                string repick = null;
                bool hard = false;
                // stand vs mounted: riders riding out of range never move the archers off their place; alone the connection ends it
                if (!g.Self && toEnemy > KeepRange * g.Reach && !IsMounted(enemy))
                {
                    if (float.IsNaN(_outOfRangeSince))
                    {
                        _outOfRangeSince = now;
                    }
                    else if (now - _outOfRangeSince >= OutOfRangeSeconds)
                    {
                        repick = "outOfRange d=" + Fmt0(toEnemy) + ">" + Fmt0(KeepRange * g.Reach) + "m";
                    }
                }
                else
                {
                    _outOfRangeSince = float.NaN;
                }
                if (repick == null && !g.Self && _mainAt.IsValid && _mainAt.Distance(mainCentre) > MainMovedRepick)
                {
                    repick = "mainMoved " + Fmt0(_mainAt.Distance(mainCentre)) + "m";
                }
                // alone the "main" is where they stand now: a held place is never ahead of itself
                if (repick == null && !g.Self && g.AheadOfMain(_move.Desired))
                {
                    repick = "ahead";
                    hard = true;
                }
                if (repick == null)
                {
                    Formation friend;
                    float share = FriendlyOverlap.Share(formation, _move.Desired, _facing, null, FriendlyOverlap.Mode.HeadingOnly, out friend);
                    if (share > FriendlyOverlap.KeepMaxShare)
                    {
                        repick = "friendOverlap " + TerrainCavalry.FormationName(friend) + " share=" + Fmt2(share);
                        hard = true;
                    }
                }
                if (repick == null && terrain != null && GroundFail(terrain, formation, _move.Desired, _facing, EdgeMin, true) != null)
                {
                    repick = "ground";
                    hard = true;
                }
                if (repick != null && (hard || now - _pickedAt >= RepickMinSeconds))
                {
                    Repick(mission, formation, terrain, g, centre, now, preferredSide, repick, out what, out why);
                    return Current(out target, out facing);
                }

                // vision
                bool arrived = ArrivedAt(formation, centre, _move.Desired, _facing, ArriveDistance);
                if (arrived && float.IsNaN(_arrivedAt))
                {
                    _arrivedAt = now;
                    _graceUntil = MathF.Max(_graceUntil, now + GraceSeconds);
                }
                if (_ladder == Ladder.Searched && now - _ladderAt >= LadderRestSeconds)
                {
                    _ladder = Ladder.None;
                }
                FormationQuerySystem q = formation.QuerySystem;
                bool ammo = q.RangedUnitRatio + q.RangedCavalryUnitRatio >= AmmoRatio;
                if (arrived && now >= _graceUntil && ammo && toEnemy <= KeepRange * g.Reach)
                {
                    float shooting = q.MakingRangedAttackRatio;
                    if (shooting >= NoShootRatio)
                    {
                        _noShootSince = float.NaN;
                        _exhausted = false;
                        if (!_keepLogged && what == null)
                        {
                            _keepLogged = true;
                            what = "support keep";
                            why = _kind + " shooting=" + Fmt2(shooting) + " enemy=" + TerrainCavalry.FormationName(enemy) + " d=" + Fmt0(toEnemy) + "m";
                        }
                        if (_ladder != Ladder.Searched)
                        {
                            _ladder = Ladder.None;
                        }
                    }
                    else if (float.IsNaN(_noShootSince))
                    {
                        _noShootSince = now;
                    }
                    else if (now - _noShootSince >= NoVisionSeconds)
                    {
                        NoVision(mission, formation, terrain, g, centre, now, preferredSide, shooting, out what, out why);
                    }
                }
                else if (!arrived)
                {
                    _noShootSince = float.NaN;
                }
                UpdateFacing(mission, formation, g, preferTarget);
                return Current(out target, out facing);
            }
            catch (System.Exception e)
            {
                // AI thread: never let an exception escape into the engine's tick (AiTickGuard)
                AiTickGuard.Report("SupportFire", e, null);
                return _active && Current(out target, out facing);
            }
        }

        /// <summary>AI thread (or wherever native cancels the behavior); true when it was active.</summary>
        internal bool Release(Formation formation, float now, string reason, out string why)
        {
            string what;
            if (!_active)
            {
                why = null;
                return false;
            }
            Drop(formation, now, reason, out what, out why);
            return true;
        }


        /// <summary>how = log text.</summary>
        private bool Connection(Mission mission, Formation formation, Formation main, float now, out Formation enemy, out string how)
        {
            enemy = null;
            how = "noMain";
            if (main == null || main.CountOfUnits <= 0)
            {
                _lastConnected = false;
                return false;
            }
            if (now - _checkedAt >= ConnectCheckSeconds || main != _checkedMain)
            {
                _checkedAt = now;
                _checkedMain = main;
                Formation nearest;
                float gap;
                NearestEngaged(mission, main, out nearest, out gap);
                bool connected = false;
                string gapText = FormationGeometry.GapText(gap);
                string text = "gap=" + gapText;
                if (nearest != null && (main == formation || ArmyComposition.IsArchers(main)))
                {
                    // alone, or beside an archer anchor (it shoots, it never closes to melee): a significant enemy within the archers' reach of the anchor
                    Vec2 own = RBMAI.Utilities.GetFormationCenter(main);
                    Vec2 other = RBMAI.Utilities.GetFormationCenter(nearest);
                    float reach = Reach(formation, nearest);
                    float d = (own.IsValid && other.IsValid) ? own.Distance(other) : float.MaxValue;
                    connected = d <= (_active ? KeepRange : TakeRange) * reach;
                    text = ((main == formation) ? "alone " : "archerAnchor ") + TerrainCavalry.FormationName(nearest) + " d=" + Fmt0(d) + "/" + Fmt0(reach) + "m";
                }
                else if (nearest != null)
                {
                    OrderType order = main.GetReadonlyMovementOrderReference().OrderType;
                    if ((order == OrderType.Charge || order == OrderType.ChargeWithTarget) && gap <= ConnectChargeGap)
                    {
                        connected = true;
                        text = "charge " + TerrainCavalry.FormationName(nearest) + " gap=" + gapText;
                    }
                    else if (gap <= MeleeProbeGap && RBMAI.Utilities.FormationFightingInMelee(main, ConnectMeleeRatio))
                    {
                        connected = true;
                        text = "melee " + TerrainCavalry.FormationName(nearest) + " gap=" + gapText;
                    }
                }
                _lastConnected = connected;
                _lastHow = text;
                if (connected)
                {
                    _connectedAt = now;
                    // engaged enemy: kept unless gone or another is RetargetMargin nearer
                    if (_enemy == null || _enemy.CountOfUnits <= 0 || _enemy == nearest || _main != main
                        || FrontGap(main, _enemy) > gap + RetargetMargin)
                    {
                        if (_enemy != nearest)
                        {
                            // a new engaged enemy: whatever was exhausted was about the old one
                            _exhausted = false;
                        }
                        _enemy = nearest;
                    }
                }
            }
            how = _lastHow ?? "-";
            bool held = _lastConnected || (_active && main == _main && now - _connectedAt < ConnectHoldSeconds);
            if (!held || _enemy == null || _enemy.CountOfUnits <= 0)
            {
                return false;
            }
            enemy = _enemy;
            return true;
        }

        private static void NearestEngaged(Mission mission, Formation main, out Formation nearest, out float nearestGap)
        {
            nearest = null;
            nearestGap = float.MaxValue;
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(main.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (enemy == null || enemy.CountOfUnits <= 0 || enemy.GetReadonlyMovementOrderReference().OrderType == OrderType.Retreat
                        || !ThreatSignificance.IsSignificant(main, enemy))
                    {
                        continue;
                    }
                    float gap = FrontGap(main, enemy);
                    if (gap < nearestGap)
                    {
                        nearestGap = gap;
                        nearest = enemy;
                    }
                }
            }
        }

        /// <summary>Footprint edge gap (either may be charging); MaxValue without a centre.</summary>
        private static float FrontGap(Formation a, Formation b)
        {
            return FormationGeometry.Gap(a, FootprintMode.ScatterAware, b, FootprintMode.ScatterAware);
        }

        /// <summary>m, centre to centre: ArcherSkirmish's effective shooting range.</summary>
        private static float Reach(Formation formation, Formation enemy)
        {
            return formation.Depth * 0.5f + formation.QuerySystem.MaximumMissileRange * 0.5f + enemy.Depth * 0.5f;
        }


        private struct Geometry
        {
            public Formation Main;
            public Formation Enemy;
            public Vec2 MainCentre;
            public Vec2 EnemyCentre;
            public Vec2 Axis;
            public Vec2 Left;
            public float Reach;
            /// <summary>Half extents of the main's footprint.</summary>
            public float MainAlong;
            public float MainAcross;
            /// <summary>Alone: the archers are their own main.</summary>
            public bool Self;
            /// <summary>Alone: a front up to this far ahead of their centre is not "ahead" (their own front).</summary>
            public float SelfFront;

            public Geometry(Formation formation, Formation main, Formation enemy, Vec2 mainCentre, Vec2 enemyCentre, Vec2 axis, float reach)
            {
                Self = main == formation;
                SelfFront = MathF.Max(formation.Depth, FriendlyOverlap.MinDepth) * 0.5f + 1f;
                Main = main;
                Enemy = enemy;
                MainCentre = mainCentre;
                EnemyCentre = enemyCentre;
                Axis = axis;
                Left = axis.LeftVec();
                Reach = reach;
                MainAlong = ExtentAlong(main, axis);
                MainAcross = ExtentAlong(main, Left);
            }

            public bool AheadOfMain(Vec2 front)
            {
                return Vec2.DotProduct(front - MainCentre, Axis) > (Self ? SelfFront : 1f);
            }

            public Vec2 Behind => MainCentre - Axis * (MainAlong + BehindGap);
        }

        private void Take(Mission mission, Formation formation, BattleTerrain terrain, Geometry g, Vec2 centre, Formation main, Formation enemy, float now,
            FormationAI.BehaviorSide preferredSide, out string what, out string why)
        {
            what = null;
            why = null;
            string standFail = StandFail(mission, formation, terrain, g, centre);
            Candidate c;
            if (standFail == null)
            {
                c = StandCandidate(formation, g, centre);
            }
            else if (!Pick(mission, formation, terrain, g, centre, preferredSide, false, false, out c)
                && !Fallback(mission, formation, terrain, g, centre, standFail, out c))
            {
                // neither a place nor a safe stand: the behavior's own order, ArcherCover answers a threat
                // (the dedupe text is built only with the log on: _noneLogged is only written there)
                _exhausted = true;
                if (RBMConfig.RBMConfig.battleTerrainLoggingEnabled)
                {
                    string none = "pick " + standFail;
                    if (_noneLogged != none)
                    {
                        _noneLogged = none;
                        what = "support none";
                        why = "search none: " + standFail + " enemy=" + TerrainCavalry.FormationName(enemy) + " " + (_lastHow ?? "-");
                    }
                }
                return;
            }
            _active = true;
            _main = main;
            FormationCohesion.SetSupportAnchor(formation, main);
            Apply(formation, g, c, now);
            _noneLogged = null;
            what = "support take";
            why = Describe(formation, g, c) + ((standFail != null) ? " here=" + standFail : "") + " " + (_lastHow ?? "-");
        }

        private void Repick(Mission mission, Formation formation, BattleTerrain terrain, Geometry g, Vec2 centre, float now,
            FormationAI.BehaviorSide preferredSide, string reason, out string what, out string why)
        {
            string standFail = StandFail(mission, formation, terrain, g, centre);
            Candidate c;
            if (standFail == null)
            {
                c = StandCandidate(formation, g, centre);
            }
            else if (!Pick(mission, formation, terrain, g, centre, preferredSide, false, false, out c)
                && !Fallback(mission, formation, terrain, g, centre, standFail, out c))
            {
                // nothing usable and no safe stand: the support ends, the behavior's own order takes over
                Drop(formation, now, "noPlace " + reason + " here=" + standFail, out what, out why);
                _exhausted = true;
                return;
            }
            // standing where the old place was is a keep, not a move
            bool same = _move.Desired.IsValid && _move.Desired.DistanceSquared(c.Front) <= 100f;
            Apply(formation, g, c, now);
            _ladder = Ladder.None;
            what = same ? "support keep" : "support move";
            why = reason + " " + Describe(formation, g, c) + " shooting=" + Fmt2(formation.QuerySystem.MakingRangedAttackRatio)
                + ((standFail != null) ? " here=" + standFail : "");
        }

        private void NoVision(Mission mission, Formation formation, BattleTerrain terrain, Geometry g, Vec2 centre, float now,
            FormationAI.BehaviorSide preferredSide, float shooting, out string what, out string why)
        {
            what = null;
            why = null;
            string spell = "noVision(shooting=" + Fmt2(shooting) + " " + Fmt0(now - _noShootSince) + "s)";
            _noShootSince = float.NaN;
            if (_ladder == Ladder.None)
            {
                _ladder = Ladder.Rotated;
                _ladderAt = now;
                EnemyConcentration.Result r;
                if (EnemyConcentration.Find(mission, formation, EnemyConcentration.Range, out r) && r.Centre.IsValid)
                {
                    Vec2 placeCentre = _move.Desired - _facing * (MathF.Max(formation.Depth, FriendlyOverlap.MinDepth) * 0.5f);
                    Vec2 toward = r.Centre - placeCentre;
                    if (toward.LengthSquared > 1f)
                    {
                        toward = toward.Normalized();
                        if (Vec2.DotProduct(toward, _facing) < MathF.Cos(RotateMinDegrees * MathF.PI / 180f))
                        {
                            _facing = toward;
                            _faceConcentration = true;
                            _graceUntil = now + GraceSeconds;
                            what = "support rotate";
                            why = spell + " " + EnemyConcentration.Describe(r);
                            _label = "support " + _kind + " rotate";
                            return;
                        }
                    }
                }
                // facing is right already: straight on to the shift
            }
            if (now - _pickedAt < RepickMinSeconds)
            {
                return;
            }
            if (_ladder == Ladder.Rotated)
            {
                _ladder = Ladder.Shifted;
                _ladderAt = now;
                Candidate c;
                if (Pick(mission, formation, terrain, g, centre, preferredSide, true, true, out c))
                {
                    Apply(formation, g, c, now);
                    _ladder = Ladder.Shifted;
                    what = "support shift";
                    why = spell + " " + Describe(formation, g, c);
                    return;
                }
            }
            if (_ladder == Ladder.Shifted)
            {
                _ladder = Ladder.Searched;
                _ladderAt = now;
                Candidate c;
                if (Search(mission, formation, terrain, g, centre, out c))
                {
                    Apply(formation, g, c, now);
                    _ladder = Ladder.Searched;
                    what = "support move";
                    why = spell + " search " + Describe(formation, g, c);
                    return;
                }
                _exhausted = true;
                what = "support none";
                why = "search none " + spell + " " + KindText();
            }
        }

        private string KindText()
        {
            return "keep=" + (_kind ?? "-");
        }

        private static Candidate StandCandidate(Formation formation, Geometry g, Vec2 centre)
        {
            Vec2 f = g.EnemyCentre - centre;
            f = (f.LengthSquared > 1f) ? f.Normalized() : g.Axis;
            float depth = MathF.Max(formation.Depth, FriendlyOverlap.MinDepth);
            return new Candidate { Kind = "stand", Front = centre + f * (depth * 0.5f), Facing = f, Travel = 0f };
        }

        /// <summary>Nothing passed every rule: the behind point when its ground is fine, no enemy is close and no friend overlaps it above KeepMaxShare ("behindRelaxed"), else stand where they are when neither ahead of the main nor unsafe ("standNoPlace"). False = neither.</summary>
        private static bool Fallback(Mission mission, Formation formation, BattleTerrain terrain, Geometry g, Vec2 centre, string standFail, out Candidate c)
        {
            Vec2 front = g.Behind;
            Vec2 f = g.EnemyCentre - front;
            if (front.IsValid && f.LengthSquared > 1f)
            {
                f = f.Normalized();
                Vec2 at = front - f * (MathF.Max(formation.Depth, FriendlyOverlap.MinDepth) * 0.5f);
                Formation friend;
                if ((terrain == null || GroundFail(terrain, formation, front, f, EdgeMin, true) == null) && EnemyFail(mission, formation, g, at, f, false) == null
                    && FriendlyOverlap.Share(formation, front, f, null, FriendlyOverlap.Mode.HeadingOnly, out friend) <= FriendlyOverlap.KeepMaxShare)
                {
                    c = new Candidate { Kind = "behindRelaxed", Front = front, Facing = f, Travel = centre.Distance(at) };
                    return true;
                }
            }
            c = StandCandidate(formation, g, centre);
            if (standFail != null && (standFail == "ahead" || standFail.StartsWith("enemy(", System.StringComparison.Ordinal)))
            {
                return false;
            }
            c.Kind = "standNoPlace";
            return true;
        }

        private static string StandFail(Mission mission, Formation formation, BattleTerrain terrain, Geometry g, Vec2 centre)
        {
            Candidate c = StandCandidate(formation, g, centre);
            if (g.AheadOfMain(c.Front))
            {
                return "ahead";
            }
            float toEnemy = centre.Distance(g.EnemyCentre);
            if (toEnemy > TakeRange * g.Reach)
            {
                return "outOfRange(" + Fmt0(toEnemy) + ">" + Fmt0(TakeRange * g.Reach) + "m)";
            }
            string unsafeText = EnemyFail(mission, formation, g, centre, c.Facing, true);
            if (unsafeText != null)
            {
                return unsafeText;
            }
            float gap = g.Self ? float.NaN : FormationCohesion.Gap(formation, c.Front, c.Facing, g.Main);
            float limit = FormationCohesion.Limit(formation, FormationCohesion.Kind.Auto) + FormationCohesion.KeepSlack;
            if (!float.IsNaN(gap) && gap > limit)
            {
                return FormationCohesion.RefusalText(g.Main, gap, limit);
            }
            Formation friend;
            if (FriendlyOverlap.Share(formation, c.Front, c.Facing, null, FriendlyOverlap.Mode.HeadingOnly, out friend) > FriendlyOverlap.KeepMaxShare)
            {
                return "overlap(" + TerrainCavalry.FormationName(friend) + ")";
            }
            // vision: shooting now, or a clear grid sight line
            if (formation.QuerySystem.MakingRangedAttackRatio < NoShootRatio && terrain != null && !terrain.SightClear(centre, g.EnemyCentre))
            {
                return "noSight";
            }
            return null;
        }

        /// <summary>needSight = a clear grid sight line to the engaged enemy is required.</summary>
        private static string PlaceFail(Mission mission, Formation formation, BattleTerrain terrain, Geometry g, Vec2 front, Vec2 f, bool needSight, float maxShare)
        {
            float depth = MathF.Max(formation.Depth, FriendlyOverlap.MinDepth);
            Vec2 at = front - f * (depth * 0.5f);
            if (g.AheadOfMain(front))
            {
                return "ahead";
            }
            float toEnemy = at.Distance(g.EnemyCentre);
            if (toEnemy > TakeRange * g.Reach)
            {
                return "range";
            }
            if (terrain != null)
            {
                string ground = GroundFail(terrain, formation, front, f, EdgeMin, true);
                if (ground != null)
                {
                    return ground;
                }
            }
            // (PlaceFail's text is only tested for null: no formatted enemy text)
            string unsafeText = EnemyFail(mission, formation, g, at, f, false);
            if (unsafeText != null)
            {
                return unsafeText;
            }
            float gap = g.Self ? float.NaN : FormationCohesion.Gap(formation, front, f, g.Main);
            float limit = FormationCohesion.Limit(formation, FormationCohesion.Kind.Auto);
            if (!float.IsNaN(gap) && gap > limit)
            {
                return "cohesion";
            }
            Formation friend;
            if (FriendlyOverlap.Share(formation, front, f, null, FriendlyOverlap.Mode.HeadingOnly, out friend) > maxShare)
            {
                return "overlap";
            }
            if (needSight && terrain != null && !terrain.SightClear(at, g.EnemyCentre))
            {
                return "noSight";
            }
            return null;
        }

        /// <summary>Walkable (front, centre, 4 of 5 footprint samples), dry when checkWet, edgeMin off the edge; null = fine. Shared with ArcherCover.</summary>
        internal static string GroundFail(BattleTerrain terrain, Formation formation, Vec2 front, Vec2 f, float edgeMin, bool checkWet)
        {
            float depth = MathF.Max(formation.Depth, FriendlyOverlap.MinDepth);
            float width = MathF.Max(formation.Width, FriendlyOverlap.MinWidth);
            Vec2 at = front - f * (depth * 0.5f);
            if (terrain.EdgeDistanceAt(at) < edgeMin || terrain.EdgeDistanceAt(front) < edgeMin)
            {
                return "edge";
            }
            if (!terrain.IsWalkable(front) || !terrain.IsWalkable(at))
            {
                return "ground";
            }
            if (checkWet && terrain.IsWet(at))
            {
                return "wet";
            }
            Vec2 side = f.LeftVec();
            int walkable = 0;
            if (terrain.IsWalkable(at + side * (width * 0.4f)))
            {
                walkable++;
            }
            if (terrain.IsWalkable(at - side * (width * 0.4f)))
            {
                walkable++;
            }
            if (terrain.IsWalkable(front + side * (width * 0.4f)))
            {
                walkable++;
            }
            if (terrain.IsWalkable(front - side * (width * 0.4f)))
            {
                walkable++;
            }
            if (terrain.IsWalkable(at - f * (depth * 0.4f)))
            {
                walkable++;
            }
            return (walkable < 4) ? "ground" : null;
        }

        /// <summary>The engaged enemy within StandMinGap, or another significant enemy melee formation within SafeEnemyGap; null = safe. Edge gaps by ORIENTED footprints along the line between the two (circles of half the larger side would make a wide line "reach" far out of its front). explain = false returns the bare "enemy".</summary>
        private static string EnemyFail(Mission mission, Formation formation, Geometry g, Vec2 at, Vec2 f, bool explain)
        {
            float ownHalfWidth = RBMAI.Utilities.RangedStableRadius(formation);
            float ownHalfDepth = MathF.Max(formation.Depth, FriendlyOverlap.MinDepth) * 0.5f;
            bool faced = f.IsValid && f.LengthSquared > 0.0001f;
            if (faced)
            {
                f = f.Normalized();
            }
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    // stand vs mounted (user rule): riders never make a place unsafe, not even the engaged enemy
                    if (enemy == null || enemy.CountOfUnits <= 0 || IsMounted(enemy))
                    {
                        continue;
                    }
                    bool engaged = enemy == g.Enemy;
                    if (!engaged && (!IsMelee(enemy) || !ThreatSignificance.IsSignificant(formation, enemy)))
                    {
                        continue;
                    }
                    Vec2 c = RBMAI.Utilities.GetFormationCenter(enemy);
                    if (!c.IsValid)
                    {
                        continue;
                    }
                    Vec2 u = at - c;
                    float d = u.Normalize();
                    float own = faced
                        ? MathF.Abs(f.x * u.x + f.y * u.y) * ownHalfDepth + MathF.Abs(-f.y * u.x + f.x * u.y) * ownHalfWidth
                        : ownHalfWidth;
                    float gap = d - ExtentAlong(enemy, u) - own;
                    if (gap < (engaged ? StandMinGap : SafeEnemyGap))
                    {
                        return explain ? "enemy(" + TerrainCavalry.FormationName(enemy) + ":" + Fmt0(gap) + "m)" : "enemy";
                    }
                }
            }
            return null;
        }

        private static bool IsMelee(Formation enemy)
        {
            if (enemy.GetReadonlyMovementOrderReference().OrderType == OrderType.Retreat)
            {
                return false;
            }
            // mounted ones are skipped by EnemyFail before this
            return !RBMAI.Utilities.IsRangedForArrangement(enemy);
        }

        private static bool IsMounted(Formation enemy)
        {
            FormationQuerySystem q = enemy.QuerySystem;
            return q.IsCavalryFormation || q.IsRangedCavalryFormation;
        }

        /// <summary>sidesOnly: left / right / beside candidates only (SHIFT).</summary>
        private bool Pick(Mission mission, Formation formation, BattleTerrain terrain, Geometry g, Vec2 centre, FormationAI.BehaviorSide preferredSide,
            bool sidesOnly, bool needSight, out Candidate best)
        {
            best = default(Candidate);
            best.Score = float.MaxValue;
            bool found = false;
            Vec2 behind = g.Behind;
            float ownHalfWidth = RBMAI.Utilities.RangedStableRadius(formation);
            float shift = MathF.Max(10f, g.MainAcross * 0.5f);
            float beside = g.MainAcross + ownHalfWidth + 10f;
            float cavalrySide = CavalrySide(mission, formation, g);
            float baseHeight = (terrain != null) ? terrain.SampleHeight(behind) : float.NaN;
            for (int k = sidesOnly ? 1 : 0; k < 5; k++)
            {
                string kind;
                Vec2 front;
                float lateral;
                float penalty;
                switch (k)
                {
                    case 0:
                        kind = "behind";
                        front = behind;
                        lateral = 0f;
                        penalty = 0f;
                        break;
                    case 1:
                        kind = "left";
                        front = behind + g.Left * shift;
                        lateral = 1f;
                        penalty = SideKindPenalty;
                        break;
                    case 2:
                        kind = "right";
                        front = behind - g.Left * shift;
                        lateral = -1f;
                        penalty = SideKindPenalty;
                        break;
                    case 3:
                        kind = "besideLeft";
                        front = g.MainCentre - g.Axis * 1.5f + g.Left * beside;
                        lateral = 1f;
                        penalty = BesideKindPenalty;
                        break;
                    default:
                        kind = "besideRight";
                        front = g.MainCentre - g.Axis * 1.5f - g.Left * beside;
                        lateral = -1f;
                        penalty = BesideKindPenalty;
                        break;
                }
                Consider(mission, formation, terrain, g, centre, kind, front, null, penalty + SidePenalty(lateral, cavalrySide, preferredSide), baseHeight, HeightBonus, needSight, ref best, ref found);
            }
            if (!sidesOnly)
            {
                TerrainSpots.SidePlan plan = TerrainSpots.ForTeam(formation.Team);
                if (plan != null && plan.Spots != null)
                {
                    foreach (TerrainSpots.Spot spot in plan.Spots)
                    {
                        if (spot == null || spot.Data == null || !spot.Position.IsValid || spot.Data.Position.Distance(behind) > SpotRange)
                        {
                            continue;
                        }
                        float lateral = Vec2.DotProduct(spot.Data.Position - g.MainCentre, g.Left);
                        Consider(mission, formation, terrain, g, centre, "spot " + spot.Id, spot.Data.Position, spot, -SpotBonus + SidePenalty(lateral, cavalrySide, preferredSide),
                            baseHeight, HeightBonus, needSight, ref best, ref found);
                    }
                }
            }
            return found;
        }

        private bool Search(Mission mission, Formation formation, BattleTerrain terrain, Geometry g, Vec2 centre, out Candidate best)
        {
            best = default(Candidate);
            best.Score = float.MaxValue;
            bool found = false;
            if (terrain == null)
            {
                return false;
            }
            Vec2 behind = g.Behind;
            float baseHeight = terrain.SampleHeight(behind);
            float cavalrySide = CavalrySide(mission, formation, g);
            for (float back = -SearchStep; back <= SearchBack; back += SearchStep)
            {
                for (float across = -SearchRadius; across <= SearchRadius; across += SearchStep)
                {
                    Vec2 front = behind - g.Axis * back + g.Left * across;
                    Consider(mission, formation, terrain, g, centre, "search", front, null, SidePenalty(across, cavalrySide, FormationAI.BehaviorSide.BehaviorSideNotSet),
                        baseHeight, SearchHeightBonus, true, ref best, ref found);
                }
            }
            return found;
        }

        private static void Consider(Mission mission, Formation formation, BattleTerrain terrain, Geometry g, Vec2 centre, string kind, Vec2 front, TerrainSpots.Spot spot,
            float penalty, float baseHeight, float heightBonus, bool needSight, ref Candidate best, ref bool found)
        {
            if (!front.IsValid)
            {
                return;
            }
            Vec2 f = g.EnemyCentre - front;
            if (f.LengthSquared < 1f)
            {
                return;
            }
            f = f.Normalized();
            if (PlaceFail(mission, formation, terrain, g, front, f, needSight, FriendlyOverlap.TakeMaxShare) != null)
            {
                return;
            }
            float depth = MathF.Max(formation.Depth, FriendlyOverlap.MinDepth);
            Vec2 at = front - f * (depth * 0.5f);
            float travel = centre.Distance(at);
            float score = travel + penalty;
            if (terrain != null)
            {
                if (!needSight && !terrain.SightClear(at, g.EnemyCentre))
                {
                    score += NoSightPenalty;
                }
                float h = terrain.SampleHeight(at);
                if (DeepWaterNavBlocker.IsPlausible(h) && DeepWaterNavBlocker.IsPlausible(baseHeight))
                {
                    score -= heightBonus * MBMath.ClampFloat(h - baseHeight, -5f, 10f);
                }
            }
            if (score < best.Score)
            {
                best = new Candidate { Kind = kind, Front = front, Facing = f, Score = score, Travel = travel, Spot = spot };
                found = true;
            }
        }

        /// <summary>Side of the main with significant enemy melee cavalry (not the engaged enemy): +1 left, -1 right, 0 none / both.</summary>
        private static float CavalrySide(Mission mission, Formation formation, Geometry g)
        {
            float sum = 0f;
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (enemy == null || enemy.CountOfUnits <= 0 || enemy == g.Enemy || !enemy.QuerySystem.IsCavalryFormation || enemy.QuerySystem.IsRangedCavalryFormation
                        || !ThreatSignificance.IsSignificant(formation, enemy))
                    {
                        continue;
                    }
                    Vec2 c = RBMAI.Utilities.GetFormationCenter(enemy);
                    if (!c.IsValid || c.Distance(g.MainCentre) > CavalryNear)
                    {
                        continue;
                    }
                    sum += Sign(Vec2.DotProduct(c - g.MainCentre, g.Left)) * enemy.CountOfUnits;
                }
            }
            return Sign(sum);
        }

        private static float Sign(float value)
        {
            return (value > 0f) ? 1f : (value < 0f) ? -1f : 0f;
        }

        private static float SidePenalty(float lateral, float cavalrySide, FormationAI.BehaviorSide preferredSide)
        {
            float side = Sign(lateral);
            if (side == 0f)
            {
                return 0f;
            }
            float penalty = 0f;
            if (cavalrySide != 0f && side == cavalrySide)
            {
                penalty += CavalrySidePenalty;
            }
            if (preferredSide == FormationAI.BehaviorSide.Left || preferredSide == FormationAI.BehaviorSide.Right)
            {
                float wanted = (preferredSide == FormationAI.BehaviorSide.Left) ? 1f : -1f;
                penalty += (side == wanted) ? -PreferredSideBonus : PreferredSideBonus;
            }
            return penalty;
        }

        /// <summary>A copy of a cached WorldPosition with ONE SetVec2MT (a spot's own one as it is).</summary>
        private void Apply(Formation formation, Geometry g, Candidate c, float now)
        {
            // a spot's own WorldPosition (its point is the spot); else the main's median near the support place, the archers' own for a stand
            WorldPosition first = (c.Spot != null) ? c.Spot.Position
                : (c.Kind == "stand" || c.Kind == "standNoPlace" || g.Main == null) ? formation.CachedMedianPosition : g.Main.CachedMedianPosition;
            WorldPosition point;
            bool rebuilt;
            _move.TryGet(c.Front, now, first, formation.CachedMedianPosition, false, true, !_move.Desired.IsValid, ApplyKeepDistance, 0f, out point, out rebuilt);
            if (rebuilt)
            {
                _arrivedAt = float.NaN;
            }
            _facing = c.Facing;
            _faceConcentration = false;
            _kind = c.Kind;
            _mainAt = g.MainCentre;
            _pickedAt = now;
            _graceUntil = now + GraceSeconds;
            _outOfRangeSince = float.NaN;
            _noShootSince = float.NaN;
            _keepLogged = false;
            _exhausted = false;
            _label = "support " + c.Kind;
        }

        private void UpdateFacing(Mission mission, Formation formation, Geometry g, Formation preferTarget)
        {
            if (_faceConcentration || !_move.Desired.IsValid)
            {
                return;
            }
            Vec2 placeCentre = _move.Desired - _facing * (MathF.Max(formation.Depth, FriendlyOverlap.MinDepth) * 0.5f);
            Vec2 aim = g.EnemyCentre;
            if (preferTarget != null && preferTarget.CountOfUnits > 0)
            {
                Vec2 c = RBMAI.Utilities.GetFormationCenter(preferTarget);
                if (c.IsValid && placeCentre.Distance(c) <= KeepRange * Reach(formation, preferTarget))
                {
                    aim = c;
                }
            }
            Vec2 toward = aim - placeCentre;
            if (toward.LengthSquared < 1f)
            {
                return;
            }
            toward = toward.Normalized();
            float off = _facing.AngleBetween(toward);
            if (off > FacingRefresh || off < -FacingRefresh)
            {
                _facing = toward;
            }
        }

        /// <summary>The archers stand at their point: footprint centre (front - facing x depth / 2) within arriveDistance + depth / 4. Shared with ArcherCover.</summary>
        internal static bool ArrivedAt(Formation formation, Vec2 centre, Vec2 point, Vec2 facing, float arriveDistance)
        {
            if (!point.IsValid || !facing.IsValid)
            {
                return false;
            }
            float depth = MathF.Max(formation.Depth, FriendlyOverlap.MinDepth);
            Vec2 at = point - facing * (depth * 0.5f);
            float arrive = arriveDistance + depth * 0.25f;
            return at.DistanceSquared(centre) <= arrive * arrive;
        }

        private bool Current(out WorldPosition target, out Vec2 facing)
        {
            target = _move.Point;
            facing = _facing;
            return _active && _move.Desired.IsValid && _facing.IsValid && target.AsVec2.IsValid;
        }

        private void Drop(Formation formation, float now, string reason, out string what, out string why)
        {
            what = "support lost";
            why = reason + " kind=" + (_kind ?? "-") + " held=" + Fmt0(now - _pickedAt) + "s";
            _active = false;
            _main = null;
            _enemy = null;
            _move.Clear();
            _facing = Vec2.Invalid;
            _kind = null;
            _mainAt = Vec2.Invalid;
            _arrivedAt = float.NaN;
            _outOfRangeSince = float.NaN;
            _noShootSince = float.NaN;
            _ladder = Ladder.None;
            _faceConcentration = false;
            _keepLogged = false;
            _noneLogged = null;
            _label = null;
            FormationCohesion.SetSupportAnchor(formation, null);
        }


        private static float ExtentAlong(Formation formation, Vec2 u)
        {
            float halfWidth = MathF.Max(formation.Width, 1f) * 0.5f;
            float halfDepth = MathF.Max(formation.Depth, 1f) * 0.5f;
            Vec2 dir = formation.Direction;
            if (!dir.IsValid || dir.LengthSquared < 0.0001f)
            {
                return MathF.Max(halfWidth, halfDepth);
            }
            dir = dir.Normalized();
            float along = dir.x * u.x + dir.y * u.y;
            float across = -dir.y * u.x + dir.x * u.y;
            return MathF.Abs(along) * halfDepth + MathF.Abs(across) * halfWidth;
        }

        private static string Describe(Formation formation, Geometry g, Candidate c)
        {
            float depth = MathF.Max(formation.Depth, FriendlyOverlap.MinDepth);
            Vec2 at = c.Front - c.Facing * (depth * 0.5f);
            return c.Kind + " d=" + Fmt0(c.Travel) + "m enemy=" + TerrainCavalry.FormationName(g.Enemy) + " range=" + Fmt0(at.Distance(g.EnemyCentre)) + "/" + Fmt0(g.Reach)
                + "m main=" + TerrainCavalry.FormationName(g.Main) + " point=" + Fmt0(c.Front.x) + "," + Fmt0(c.Front.y);
        }

        private static string Fmt0(float value)
        {
            return TerrainBattlePosition.Fmt0(value);
        }

        private static string Fmt2(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// One behavior's ArcherCover + SupportFire and the glue the archer behaviors share: evaluate, log, track the on/off flag.
    /// The caller issues the orders (its CurrentOrder / CurrentFacingOrder) and its own start / end steps. AI thread.
    /// </summary>
    internal sealed class CoverSupport
    {
        internal enum Step
        {
            Off,
            /// <summary>Was on, now off.</summary>
            Ended,
            /// <summary>Was off, now on.</summary>
            Started,
            Held
        }

        internal readonly ArcherCover Cover = new ArcherCover();
        internal readonly SupportFire Support = new SupportFire();
        private readonly string _behavior;
        private readonly string _pointKey;
        private bool _covering;
        private bool _supporting;

        /// <summary>behavior = the BEHAVIOR line's behavior name; pointKey = its point field's name ("spot" for the free archer behaviors, "point" for the holds).</summary>
        internal CoverSupport(string behavior, string pointKey)
        {
            _behavior = behavior;
            _pointKey = pointKey;
        }

        internal bool Covering => _covering;

        internal bool Supporting => _supporting;

        internal static bool IsOn(Step step)
        {
            return step == Step.Started || step == Step.Held;
        }

        internal Step CoverStep(BehaviorComponent owner, Formation formation, bool atPost, float now, out WorldPosition target, out Vec2 facing)
        {
            string what;
            string why;
            // the archer stand never steps back
            if (RBMBehaviorArcherStand.Owns(formation))
            {
                target = WorldPosition.Invalid;
                facing = Vec2.Invalid;
                ReleaseCover(formation, "archerStand");
                return Next(false, ref _covering);
            }
            // supporting the engaged main: it counts as cover under Charge too
            Cover.SetSupport(Support.Main, Support.Refuge(formation));
            bool covering = Cover.Evaluate(formation, formation.AI != null && formation.AI.ActiveBehavior == owner, atPost, out target, out facing, out what, out why);
            if (what != null)
            {
                Enqueue(formation, now, what, "cover", why);
            }
            return Next(covering, ref _covering);
        }

        internal Step SupportStep(BehaviorComponent owner, Formation formation, Formation preferTarget, Vec2 post, Vec2 postFacing, FormationAI.BehaviorSide preferredSide,
            float now, out WorldPosition target, out Vec2 facing)
        {
            string what;
            string why;
            if (RBMBehaviorArcherStand.Owns(formation))
            {
                target = WorldPosition.Invalid;
                facing = Vec2.Invalid;
                ReleaseSupport(formation, "archerStand");
                return Next(false, ref _supporting);
            }
            bool supporting = Support.Evaluate(formation, formation.AI != null && formation.AI.ActiveBehavior == owner, preferTarget, post, postFacing, preferredSide,
                out target, out facing, out what, out why);
            if (what != null)
            {
                Enqueue(formation, now, what, "support", why);
            }
            return Next(supporting, ref _supporting);
        }

        /// <summary>Ends a cover (AI thread or wherever native cancels the behavior); true when it was covering.</summary>
        internal bool ReleaseCover(Formation formation, string reason)
        {
            Mission mission = Mission.Current;
            string why;
            if (mission != null && Cover.Release(mission.CurrentTime, reason, out why) && formation != null)
            {
                Enqueue(formation, mission.CurrentTime, "cover release", "cover", why);
            }
            bool was = _covering;
            _covering = false;
            return was;
        }

        /// <summary>Ends the support fire; true when it was supporting.</summary>
        internal bool ReleaseSupport(Formation formation, string reason)
        {
            Mission mission = Mission.Current;
            string why;
            if (mission != null && Support.Release(formation, mission.CurrentTime, reason, out why) && formation != null)
            {
                Enqueue(formation, mission.CurrentTime, "support lost", "support", why);
            }
            bool was = _supporting;
            _supporting = false;
            return was;
        }

        private static Step Next(bool on, ref bool flag)
        {
            if (!on)
            {
                if (flag)
                {
                    flag = false;
                    return Step.Ended;
                }
                return Step.Off;
            }
            Step step = flag ? Step.Held : Step.Started;
            flag = true;
            return step;
        }

        private void Enqueue(Formation formation, float now, string what, string point, string why)
        {
            TerrainLog.Behavior(formation.Team, formation, now, _behavior, what, _pointKey, point, why);
        }
    }
}
