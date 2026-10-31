using System.Collections.Concurrent;
using System.Globalization;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// Foot formations stay near their main infantry (user rule). Used for AI-controlled FOOT formations only (never cavalry /
    /// horse archers / player-controlled): spot take/keep (TerrainSpots), fixed per-plan places (Pull after PlaceShift:
    /// HoldPosition/HoldChoke Archers, ReservePlug), ArcherFlank/ArcherSkirmish targets, ArcherCover fallback steps,
    /// PlaceShift, and infantry flank guards (ClampGuard, stateless).
    ///
    /// ANCHOR: the team's MAIN infantry = largest melee infantry formation of its own team, AI- or PLAYER-controlled (the player's
    /// line is only READ, never ordered), held with hysteresis (TeamMain: switch at MainSwitchRatio x men); the formation itself
    /// is left out; a caller may name one. None while the team's infantry does not lead (ArmyComposition's plan: smoothed weighted share
    /// MainEnterStrengthShare, kept to MainKeepStrengthShare; troop type); then the position anchor (ArmyComposition.AnchorFor). No anchor while the anchor is under Charge / ChargeWithTarget / Retreat (ResolveAnchor),
    /// except a ranged formation in SupportFire keeps its engaged main (SetSupportAnchor).
    /// Footprints by the front-centre rule (the Move point is the FRONT-centre, ranks behind it): where the anchor stands NOW and,
    /// with a valid cached OrderPosition and no Charge/Retreat order, where it is HEADING.
    ///
    /// GAP: edge gap from the candidate footprint to the NEARER anchor footprint (rectangle distance; 0 when overlapping).
    ///
    /// LIMITS: defender/attacker x ranged/melee constants; the defender's hidden reserve plug ReserveLimit.
    ///
    /// EXEMPT: player-controlled, mounted (cavalry/HA or >= half mounted men), under Charge/ChargeWithTarget/Retreat, >= EngagedMeleeRatio
    /// in melee (checked only when a pull/release would happen); covering archers; the hedgehog's archers.
    ///
    /// PULL: a place beyond the limit is moved along the line toward the anchor, PullStep m at a time, to the first point within
    /// limit - PullInside that is walkable (BattleTerrain.Current), EdgeMin off the map edge, preferring no friend overlap. The target
    /// is a copy of the place's WorldPosition with ONE SetVec2MT, cached and rebuilt only on place/facing/anchor moves. No terrain
    /// snapshot = no pull. Log: BEHAVIOR what=cohesion (pull/unpull/noPull).
    ///
    /// THREADING: AI-thread safe (cached data, immutable terrain snapshot); guard log latch and per-team main latch are
    /// ConcurrentDictionaries cleared in Clear (MissionStartReset).
    /// </summary>
    internal static class FormationCohesion
    {

        // Limits are edge gaps (m) from the main infantry.
        internal const float DefenderRangedLimit = 40f;
        internal const float DefenderMeleeLimit = 30f;
        internal const float AttackerRangedLimit = 70f;
        internal const float AttackerMeleeLimit = 50f;
        internal const float ReserveLimit = 80f; // the defender's reserve plug
        internal const float KeepSlack = 15f; // a held archer spot is let go only beyond limit + this
        internal const float PullInside = 3f; // m inside the limit (no flicker at the boundary)
        private const float PullStep = 4f; // m
        private const int PullMaxSteps = 80;
        internal const float RebuildDistance = 5f; // a Pull is decided again when the place moved this far ...
        internal const float AnchorRebuildDistance = 10f; // ... or the anchor's order position / centre
        private const float AnchorResize = 5f; // ... or its width / depth changed
        private const float TurnCos = 0.985f; // ... or the place's facing turned ~10 deg
        internal const float EngagedMeleeRatio = 0.1f; // never pulled / released (Utilities.FormationFightingInMelee)
        private const float FrozenSeconds = 2f; // a frozen Pull (engaged / charging) looks again after this
        private const float EdgeMin = 10f; // from the map edge
        private const float LogMoveDistance = 10f; // m

        internal enum Kind
        {
            Auto,
            /// <summary>An attacker's reserve uses the attacker melee limit.</summary>
            Reserve
        }

        internal const float MainSwitchRatio = 1.2f; // x men to replace the held main (TeamMain)

        /// <summary>1 = pulled, 2 = refused.</summary>
        private static readonly ConcurrentDictionary<Formation, byte> GuardState = new ConcurrentDictionary<Formation, byte>();

        private static readonly ConcurrentDictionary<Team, Formation> MainLatch = new ConcurrentDictionary<Team, Formation>();

        /// <summary>Ranged formation -> its engaged main, its anchor even under Charge. Written by SupportFire (AI thread).</summary>
        private static readonly ConcurrentDictionary<Formation, Formation> SupportAnchors = new ConcurrentDictionary<Formation, Formation>();

        /// <summary>Main thread: MissionStartReset.</summary>
        internal static void Clear()
        {
            GuardState.Clear();
            MainLatch.Clear();
            SupportAnchors.Clear();
        }

        /// <summary>AI thread (SupportFire): <paramref name="formation"/> supports <paramref name="main"/> (null: no longer).</summary>
        internal static void SetSupportAnchor(Formation formation, Formation main)
        {
            if (formation == null)
            {
                return;
            }
            Formation old;
            if (main == null)
            {
                SupportAnchors.TryRemove(formation, out old);
            }
            else
            {
                SupportAnchors[formation] = main;
            }
        }


        /// <summary>Melee infantry by the old state test AND by troop type (a dry archer formation never counts).</summary>
        internal static bool IsMeleeInfantry(Formation f)
        {
            return f != null && f.CountOfUnits > 0 && f.QuerySystem.IsInfantryFormation && !RBMAI.Utilities.IsRangedForArrangement(f)
                && ArmyComposition.IsMeleeInfantryType(f);
        }

        // Anchor needs min(MainMinMen, ceil(MainMinShare x team men)) men, at least 1.
        private const int MainMinMen = 8;
        private const float MainMinShare = 0.25f;

        internal static int MinMainMen(Team team)
        {
            int teamMen = (team?.QuerySystem != null) ? team.QuerySystem.MemberCount : 0;
            return System.Math.Max(1, System.Math.Min(MainMinMen, (int)System.Math.Ceiling(MainMinShare * teamMen)));
        }

        /// <summary>AI thread: largest melee infantry with at least MinMainMen men while the team's infantry leads (ArmyComposition's plan), else null (no hysteresis on the pick). aiOnly: AI-controlled only.</summary>
        internal static Formation LargestMeleeInfantry(Team team, bool aiOnly)
        {
            return (team == null) ? null : Largest(team, null, aiOnly, ArmyComposition.InfantryLeads(team));
        }

        /// <summary>Largest melee infantry with at least MinMainMen men, or null; null too while the infantry does not lead.</summary>
        private static Formation Largest(Team team, Formation except, bool aiOnly, bool leads)
        {
            if (!leads)
            {
                return null;
            }
            Formation best = null;
            foreach (Formation f in team.FormationsIncludingSpecialAndEmpty)
            {
                if (f == null || f == except || f.CountOfUnits <= 0 || (aiOnly && !f.IsAIControlled) || (best != null && f.CountOfUnits <= best.CountOfUnits))
                {
                    continue;
                }
                if (IsMeleeInfantry(f))
                {
                    best = f;
                }
            }
            return (best != null && best.CountOfUnits >= MinMainMen(team)) ? best : null;
        }

        /// <summary>AI thread: held with hysteresis (AI- or player-controlled); read-only, never order or pull it. Infantry leads by ArmyComposition's plan.</summary>
        internal static Formation TeamMain(Team team)
        {
            return (team == null) ? null : TeamMainWith(team, ArmyComposition.InfantryLeads(team));
        }

        /// <summary>AI thread: TeamMain with the verdict given (ArmyComposition's build passes the one it is deciding).</summary>
        internal static Formation TeamMainWith(Team team, bool leads)
        {
            Formation largest = Largest(team, null, false, leads);
            Formation held;
            if (largest == null)
            {
                MainLatch.TryRemove(team, out held);
                return null;
            }
            if (MainLatch.TryGetValue(team, out held) && held != largest && held.Team == team && IsMeleeInfantry(held)
                && held.CountOfUnits >= MinMainMen(team) && largest.CountOfUnits < MainSwitchRatio * held.CountOfUnits)
            {
                return held;
            }
            MainLatch[team] = largest;
            return largest;
        }

        /// <summary>AI thread: the main infantry for the formation (TeamMain, itself left out), null without one. aiOnly: largest AI-controlled melee infantry, no hysteresis.</summary>
        internal static Formation MainInfantry(Formation formation, bool aiOnly = false)
        {
            Team team = formation?.Team;
            if (team == null)
            {
                return null;
            }
            bool leads = ArmyComposition.InfantryLeads(team);
            if (aiOnly)
            {
                return Largest(team, formation, true, leads);
            }
            Formation main = TeamMainWith(team, leads);
            return (main == formation) ? Largest(team, formation, false, leads) : main;
        }

        /// <summary>The named anchor when usable, else the main infantry, else (no main) the position anchor; null while it is under a Charge / ChargeWithTarget / Retreat order.</summary>
        private static Formation ResolveAnchor(Formation formation, Formation anchorOverride)
        {
            // SupportFire: a formation supporting its engaged main keeps it as anchor under Charge too
            Formation supported;
            if (anchorOverride == null && formation != null && SupportAnchors.TryGetValue(formation, out supported)
                && supported != null && supported != formation && supported.CountOfUnits > 0 && supported.Team == formation.Team)
            {
                return supported;
            }
            Formation anchor = (anchorOverride != null && anchorOverride != formation && anchorOverride.CountOfUnits > 0)
                ? anchorOverride
                : (MainInfantry(formation) ?? ArmyComposition.AnchorFor(formation));
            return (anchor != null && OrderExemption(anchor) == null) ? anchor : null;
        }

        internal static float Limit(Formation formation, Kind kind)
        {
            bool defender = formation?.Team != null && formation.Team.Side == BattleSideEnum.Defender;
            float scale = ArmyComposition.Lerp(1f, ArmyComposition.CavalryLimitFactor,ArmyComposition.KCav(formation?.Team));
            if (kind == Kind.Reserve)
            {
                return (defender ? ReserveLimit : AttackerMeleeLimit) * scale;
            }
            bool ranged = RBMAI.Utilities.IsRangedForArrangement(formation);
            return (defender ? (ranged ? DefenderRangedLimit : DefenderMeleeLimit) : (ranged ? AttackerRangedLimit : AttackerMeleeLimit)) * scale;
        }

        /// <summary>Why the formation is never held to a limit (player | mounted | empty), or null.</summary>
        internal static string StaticExemption(Formation formation)
        {
            if (formation == null || formation.CountOfUnits <= 0 || formation.Team == null)
            {
                return "empty";
            }
            if (!formation.IsAIControlled)
            {
                return "player";
            }
            FormationQuerySystem q = formation.QuerySystem;
            if (q.IsCavalryFormation || q.IsRangedCavalryFormation || q.CavalryUnitRatio + q.RangedCavalryUnitRatio >= 0.5f)
            {
                return "mounted";
            }
            return null;
        }

        /// <summary>Under a Charge / ChargeWithTarget / Retreat order: "order=Charge" etc., else null.</summary>
        internal static string OrderExemption(Formation formation)
        {
            // constant texts: hot predicate, no allocation
            switch (formation.GetReadonlyMovementOrderReference().OrderType)
            {
                case OrderType.Charge:
                    return "order=Charge";
                case OrderType.ChargeWithTarget:
                    return "order=ChargeWithTarget";
                case OrderType.Retreat:
                    return "order=Retreat";
            }
            // walking to its charge (ChargeGuard advance Move) is the charge
            return ChargeGuard.IsAdvancingToCharge(formation) ? "order=chargeAdvance" : null;
        }

        /// <summary>Iterates its men: call only when it matters.</summary>
        internal static bool Engaged(Formation formation)
        {
            return RBMAI.Utilities.FormationFightingInMelee(formation, EngagedMeleeRatio);
        }


        private static bool AnchorBoxes(Formation anchor, out Footprint now, out bool hasNow, out Footprint heading, out bool hasHeading)
        {
            hasNow = FormationGeometry.TryGet(anchor, FootprintMode.Current, out now);
            // the destination rule leaves out charges / retreats / charge advances, as OrderExemption
            hasHeading = FormationGeometry.TryGetDestination(anchor, out heading);
            return hasNow || hasHeading;
        }

        /// <summary>AI thread: edge gap (m) to the nearer anchor footprint (facing invalid: own Direction); NaN without usable geometry.</summary>
        internal static float Gap(Formation formation, Vec2 front, Vec2 facing, Formation anchor, out Vec2 toward)
        {
            toward = Vec2.Invalid;
            if (formation == null || anchor == null || !front.IsValid)
            {
                return float.NaN;
            }
            Vec2 f = FriendlyOverlap.Unit(facing);
            if (!f.IsValid)
            {
                f = FriendlyOverlap.Unit(formation.Direction);
                if (!f.IsValid)
                {
                    return float.NaN;
                }
            }
            Footprint now;
            Footprint heading;
            bool hasNow;
            bool hasHeading;
            if (!AnchorBoxes(anchor, out now, out hasNow, out heading, out hasHeading))
            {
                return float.NaN;
            }
            Footprint own = FormationGeometry.AtFront(formation, front, f);
            float best = float.MaxValue;
            if (hasNow)
            {
                best = FormationGeometry.Gap(own, now);
                toward = now.Centre;
            }
            if (hasHeading)
            {
                float g = FormationGeometry.Gap(own, heading);
                if (g < best)
                {
                    best = g;
                    toward = heading.Centre;
                }
            }
            return best;
        }

        internal static float Gap(Formation formation, Vec2 front, Vec2 facing, Formation anchor)
        {
            Vec2 toward;
            return Gap(formation, front, facing, anchor, out toward);
        }

        /// <summary>AI thread: NaN without one.</summary>
        internal static float CurrentGap(Formation formation, Formation anchor)
        {
            if (formation == null || anchor == null)
            {
                return float.NaN;
            }
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(formation);
            Vec2 f = FriendlyOverlap.Unit(formation.Direction);
            if (!centre.IsValid || !f.IsValid)
            {
                return float.NaN;
            }
            float depth = MathF.Max(formation.Depth, FriendlyOverlap.MinDepth);
            return Gap(formation, centre + f * (depth * 0.5f), f, anchor);
        }

        /// <summary>AI thread: false only when beyond limit + slack; true when within, exempt or anchorless. Melee / orders are the caller's business.</summary>
        internal static bool Within(Formation formation, Vec2 front, Vec2 facing, Kind kind, Formation anchorOverride, float slack, out Formation anchor, out float gap, out float limit)
        {
            anchor = null;
            gap = float.NaN;
            limit = float.NaN;
            if (StaticExemption(formation) != null)
            {
                return true;
            }
            anchor = ResolveAnchor(formation, anchorOverride);
            if (anchor == null)
            {
                return true;
            }
            limit = Limit(formation, kind);
            gap = Gap(formation, front, facing, anchor);
            return float.IsNaN(gap) || gap <= limit + slack;
        }


        /// <summary>AI thread: note = " overlap" when only an overlapping point was walkable. False with refused = ground | noLine.</summary>
        internal static bool TryPullPoint(Formation formation, Vec2 front, Vec2 facing, Formation anchor, float limit, BattleTerrain terrain,
            out Vec2 pulled, out float gapAfter, out string note, out string refused)
        {
            pulled = Vec2.Invalid;
            gapAfter = float.NaN;
            note = null;
            refused = null;
            Vec2 f = FriendlyOverlap.Unit(facing);
            if (!f.IsValid)
            {
                f = FriendlyOverlap.Unit(formation.Direction);
            }
            Vec2 toward;
            float gap = Gap(formation, front, f, anchor, out toward);
            if (terrain == null || !f.IsValid || float.IsNaN(gap) || !toward.IsValid)
            {
                refused = "noLine";
                return false;
            }
            float depth = MathF.Max(formation.Depth, FriendlyOverlap.MinDepth);
            Vec2 centre = front - f * (depth * 0.5f);
            Vec2 u = toward - centre;
            float length = u.Normalize();
            if (length < 1f)
            {
                refused = "noLine";
                return false;
            }
            float target = MathF.Max(limit - PullInside, 0f);
            // the gap shrinks at most 1 m per metre moved: start at the least move that can do it
            float s = MathF.Max(gap - target, 0f);
            Vec2 firstWalkable = Vec2.Invalid;
            float firstGap = float.NaN;
            for (int i = 0; i < PullMaxSteps && s <= length; i++, s += PullStep)
            {
                Vec2 p = front + u * s;
                float g = Gap(formation, p, f, anchor);
                if (float.IsNaN(g) || g > target)
                {
                    continue;
                }
                Vec2 c = p - f * (depth * 0.5f);
                if (!terrain.IsWalkable(p) || !terrain.IsWalkable(c) || terrain.EdgeDistanceAt(c) < EdgeMin)
                {
                    continue;
                }
                Formation friend;
                if (FriendlyOverlap.Share(formation, p, f, null, FriendlyOverlap.Mode.HeadingOnly, out friend) <= FriendlyOverlap.TakeMaxShare)
                {
                    pulled = p;
                    gapAfter = g;
                    return true;
                }
                if (!firstWalkable.IsValid)
                {
                    firstWalkable = p;
                    firstGap = g;
                }
            }
            if (firstWalkable.IsValid)
            {
                pulled = firstWalkable;
                gapAfter = firstGap;
                note = " overlap";
                return true;
            }
            refused = "ground";
            return false;
        }

        /// <summary>AI thread, infantry flank guard (stateless: recomputed every tick). Logged once per change.</summary>
        internal static bool ClampGuard(Formation guard, Formation main, Vec2 front, Vec2 facing, out Vec2 pulled)
        {
            pulled = front;
            Mission mission = Mission.Current;
            if (guard == null || mission == null)
            {
                return false;
            }
            float now = mission.CurrentTime;
            Formation anchor;
            float gap;
            float limit;
            byte state;
            if (Within(guard, front, facing, Kind.Auto, main, 0f, out anchor, out gap, out limit))
            {
                if (GuardState.TryRemove(guard, out state) && state == 1)
                {
                    TerrainSpots.EnqueueBehavior(guard.Team, guard, now, "ProtectFlank", "cohesion", "guard",
                        "unpull " + ((anchor != null) ? DescribeWithin(anchor, gap, limit) : "noAnchor"));
                }
                return false;
            }
            BattleTerrain terrain = BattleTerrain.Current;
            if (terrain == null || OrderExemption(guard) != null || Engaged(guard))
            {
                return false;
            }
            Vec2 p;
            float gapAfter;
            string note;
            string refused;
            if (!TryPullPoint(guard, front, facing, anchor, limit, terrain, out p, out gapAfter, out note, out refused))
            {
                if (!GuardState.TryGetValue(guard, out state) || state != 2)
                {
                    GuardState[guard] = 2;
                    TerrainSpots.EnqueueBehavior(guard.Team, guard, now, "ProtectFlank", "cohesion", "guard", "noPull " + Describe(anchor, gap, limit) + " " + refused);
                }
                return false;
            }
            pulled = p;
            if (!GuardState.TryGetValue(guard, out state) || state != 1)
            {
                GuardState[guard] = 1;
                TerrainSpots.EnqueueBehavior(guard.Team, guard, now, "ProtectFlank", "cohesion", "guard",
                    "pull " + Fmt0(front.Distance(p)) + "m " + Describe(anchor, gap, limit) + "->" + Fmt0(gapAfter) + "m" + note);
            }
            return true;
        }

        /// <summary>One behavior's pulled place. AI thread only. Resolve every order calculation (after PlaceShift for fixed places); Reset on re-assignment.</summary>
        internal sealed class Pull
        {
            private enum PullState
            {
                None,
                Pulled,
                Refused
            }

            private PullState _state = PullState.None;
            private Vec2 _source = Vec2.Invalid;
            private Vec2 _facing = Vec2.Invalid;
            private Formation _anchor;
            private Vec2 _anchorOrder = Vec2.Invalid;
            private Vec2 _anchorCentre = Vec2.Invalid;
            private float _anchorWidth = float.NaN;
            private float _anchorDepth = float.NaN;
            private Vec2 _pulled = Vec2.Invalid;
            private Vec2 _logged = Vec2.Invalid;
            private WorldPosition _target = WorldPosition.Invalid;
            private float _frozenUntil = float.NegativeInfinity;

            internal bool IsPulled => _state == PullState.Pulled;

            internal void Reset()
            {
                _state = PullState.None;
                _source = Vec2.Invalid;
                _facing = Vec2.Invalid;
                _anchor = null;
                _anchorOrder = Vec2.Invalid;
                _anchorCentre = Vec2.Invalid;
                _anchorWidth = float.NaN;
                _anchorDepth = float.NaN;
                _pulled = Vec2.Invalid;
                _logged = Vec2.Invalid;
                _target = WorldPosition.Invalid;
                _frozenUntil = float.NegativeInfinity;
            }

            /// <summary>AI thread: the planned place or a cached pulled copy. what / why = a log note when the decision changed. anchorOverride null = the main infantry.</summary>
            internal WorldPosition Resolve(Formation formation, WorldPosition planned, Vec2 facing, Kind kind, Formation anchorOverride,
                out Vec2 point, out string what, out string why)
            {
                what = null;
                why = null;
                Vec2 source = planned.AsVec2;
                point = source;
                Mission mission = Mission.Current;
                if (formation == null || mission == null || !source.IsValid)
                {
                    return planned;
                }
                Vec2 f = FriendlyOverlap.Unit(facing);
                if (!f.IsValid)
                {
                    f = FriendlyOverlap.Unit(formation.Direction);
                    if (!f.IsValid)
                    {
                        return planned;
                    }
                }
                string exempt = StaticExemption(formation);
                Formation anchor = (exempt == null) ? ResolveAnchor(formation, anchorOverride) : null;
                BattleTerrain terrain = BattleTerrain.Current;
                if (exempt != null || anchor == null || terrain == null)
                {
                    if (_state == PullState.Pulled)
                    {
                        what = "cohesion";
                        why = "unpull " + (exempt ?? ((anchor == null) ? "noAnchor" : "noTerrain"));
                    }
                    Reset();
                    return planned;
                }
                float now = mission.CurrentTime;
                Vec2 anchorCentre = RBMAI.Utilities.GetFormationCenter(anchor);
                Vec2 anchorOrder;
                if (!FriendlyOverlap.HeadingPoint(anchor, out anchorOrder))
                {
                    anchorOrder = Vec2.Invalid;
                }
                bool rebuild = !_source.IsValid || _source.DistanceSquared(source) > RebuildDistance * RebuildDistance
                    || !_facing.IsValid || Vec2.DotProduct(_facing, f) < TurnCos
                    || anchor != _anchor
                    || Moved(_anchorCentre, anchorCentre) || Moved(_anchorOrder, anchorOrder)
                    || float.IsNaN(_anchorWidth) || MathF.Abs(anchor.Width - _anchorWidth) > AnchorResize || MathF.Abs(anchor.Depth - _anchorDepth) > AnchorResize;
                if (!rebuild || now < _frozenUntil)
                {
                    return Current(planned, out point);
                }
                float limit = Limit(formation, kind);
                Vec2 toward;
                float gap = Gap(formation, source, f, anchor, out toward);
                bool beyond = !float.IsNaN(gap) && gap > limit;
                if (beyond || _state == PullState.Pulled)
                {
                    // never pulled / released while charging, retreating or fighting: looked at again in FrozenSeconds
                    if (OrderExemption(formation) != null || Engaged(formation))
                    {
                        _frozenUntil = now + FrozenSeconds;
                        return Current(planned, out point);
                    }
                }
                PullState old = _state;
                _source = source;
                _facing = f;
                _anchor = anchor;
                _anchorCentre = anchorCentre;
                _anchorOrder = anchorOrder;
                _anchorWidth = anchor.Width;
                _anchorDepth = anchor.Depth;
                if (!beyond)
                {
                    _state = PullState.None;
                    _pulled = Vec2.Invalid;
                    _logged = Vec2.Invalid;
                    _target = WorldPosition.Invalid;
                    if (old == PullState.Pulled)
                    {
                        what = "cohesion";
                        why = "unpull " + DescribeWithin(anchor, gap, limit);
                    }
                    return planned;
                }
                Vec2 p;
                float gapAfter;
                string note;
                string refused;
                if (!TryPullPoint(formation, source, f, anchor, limit, terrain, out p, out gapAfter, out note, out refused))
                {
                    _state = PullState.Refused;
                    _pulled = Vec2.Invalid;
                    _logged = Vec2.Invalid;
                    _target = WorldPosition.Invalid;
                    if (old != PullState.Refused)
                    {
                        what = "cohesion";
                        why = "noPull " + Describe(anchor, gap, limit) + " " + refused;
                    }
                    return planned;
                }
                if (!(old == PullState.Pulled && _pulled.IsValid && _pulled.DistanceSquared(p) < 0.25f))
                {
                    // ONE SetVec2MT on a fresh copy, kept until a rebuild (native caches the order position by its Vec2)
                    WorldPosition target = planned;
                    target.SetVec2MT(p);
                    _target = target;
                    _pulled = p;
                }
                _state = PullState.Pulled;
                if (old != PullState.Pulled || !_logged.IsValid || _logged.DistanceSquared(p) > LogMoveDistance * LogMoveDistance)
                {
                    _logged = p;
                    what = "cohesion";
                    why = "pull " + Fmt0(source.Distance(p)) + "m " + Describe(anchor, gap, limit) + "->" + Fmt0(gapAfter) + "m" + note;
                }
                return Current(planned, out point);
            }

            private WorldPosition Current(WorldPosition planned, out Vec2 point)
            {
                if (_state == PullState.Pulled && _pulled.IsValid)
                {
                    point = _pulled;
                    return _target;
                }
                point = planned.AsVec2;
                return planned;
            }

            private static bool Moved(Vec2 was, Vec2 now)
            {
                if (was.IsValid != now.IsValid)
                {
                    return true;
                }
                return was.IsValid && was.DistanceSquared(now) > AnchorRebuildDistance * AnchorRebuildDistance;
            }
        }


        /// <summary>"f0:Infantry gap=63m>40m".</summary>
        internal static string Describe(Formation anchor, float gap, float limit)
        {
            return TerrainCavalry.FormationName(anchor) + " gap=" + Fmt0(gap) + "m>" + Fmt0(limit) + "m" + KCavNote(anchor);
        }

        /// <summary>"f0:Infantry gap=35m<=40m".</summary>
        internal static string DescribeWithin(Formation anchor, float gap, float limit)
        {
            return TerrainCavalry.FormationName(anchor) + " gap=" + Fmt0(gap) + "m<=" + Fmt0(limit) + "m" + KCavNote(anchor);
        }

        // the limit was scaled by kCav (Limit)
        private static string KCavNote(Formation anchor)
        {
            float k = ArmyComposition.KCav(anchor?.Team);
            return (k > 0f) ? " kCav=" + ArmyComposition.F2(k) : "";
        }

        /// <summary>Refusal text for the per-point refusal lists (no spaces, no commas): "cohesion(f0:Infantry:63>40m)".</summary>
        internal static string RefusalText(Formation anchor, float gap, float limit)
        {
            return "cohesion(" + TerrainCavalry.FormationName(anchor) + ":" + Fmt0(gap) + ">" + Fmt0(limit) + "m)";
        }

        private static string Fmt0(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value) || value == float.MaxValue) ? "-" : value.ToString("0", CultureInfo.InvariantCulture);
        }
    }
}
