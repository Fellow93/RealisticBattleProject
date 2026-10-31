using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// User rule: infantry charges only when its target is within ChargeStartDistance (40 m). An AI (and player) foot formation may DECIDE to engage from any
    /// distance, but until the target is close it ADVANCES in formation: a Move to a point StandOff m short of the target's near edge,
    /// facing it, keeping its arrangement and pace. Cavalry, sieges and sallies are never touched.
    ///
    /// <see cref="Apply"/> is called from RBM's Formation.SetMovementOrder prefix (Priority.First, after the Charge -> ChargeToTarget
    /// conversion, so OrderReactionHold's stamp prefix sees the applied order). It changes only the order handed to the formation, never the
    /// behavior's CurrentOrder, so behaviors keep re-issuing their charge and get the same cached Move point back.
    ///
    /// Target: ChargeToTarget's own, else the significant enemy formation with the smallest edge gap. Edge gap = centre distance minus both
    /// footprints' half extents ALONG the line between the centres (a rectangle's support), so a wide line counts its front.
    ///
    /// The real charge goes when the target (or a nearer significant enemy FOOT formation, which gets the charge) is within
    /// ChargeStartDistance, or the formation is at least MeleeShareGo in melee (a nearer significant foot formation gets the charge), or it
    /// already charges (hysteresis: kept until the gap stays above ChargeKeepDistance for ChargeKeepSeconds). Never the plain charge: under
    /// it every man fights whoever is nearest and the block scatters after passing riders. Enemy cavalry riding near, or in melee with the
    /// block while the target is still far, does NOT release it (the formed block fights the riders in reach). A fleeing target is followed
    /// at the walk, never chased.
    ///
    /// Move point: target centre - u x (its half extent along u + StandOff), u = own centre -> target centre (the order position is the
    /// FRONT-centre). ONE SetVec2MT on a copy of the target's cached median WorldPosition (native caches the order position by its exact
    /// Vec2), rebuilt after CachedMovePoint's RebuildDistance m and RebuildSeconds or a target change. Facing LookAtDirection(u), re-aimed after ~10 degrees;
    /// behaviors that set their own facing keep theirs; when the intent ends a facing still ours goes back to LookAtEnemy.
    ///
    /// Safety net: a Formation.Tick postfix re-applies the active behavior's own CurrentOrder every RecheckSeconds while advancing (for
    /// behaviors that do not re-issue their charge, and for a target that died).
    ///
    /// Readers: <see cref="IsAdvancingToCharge"/> / <see cref="IsChargeIntent"/> let code that asks "is it charging" treat the advance as one;
    /// they also count AI riders' own formed approach (FormedApproach.Target: CavalryCharge, React strikes), which this guard never touches.
    ///
    /// THREADING: SetMovementOrder runs on the async AI thread or the main thread (control changes); Formation.Tick on the AI thread.
    /// Cached formation data only; one SetVec2MT on a fresh copy of a cached WorldPosition; no engine spatial query, no WorldPosition
    /// construction, no MBRandom. Per-formation state in a ConcurrentDictionary, cleared in MissionStartReset.
    /// </summary>
    internal static class ChargeGuard
    {
        // User rule: real charge only within 40 m (edge gap; was 50 until 2026-10-30). A released charge is kept until the gap stays above ChargeKeepDistance for ChargeKeepSeconds.
        internal const float ChargeStartDistance = 40f; // m
        internal const float ChargeKeepDistance = 75f; // m
        internal const float ChargeKeepSeconds = 3f;

        internal const float MeleeShareGo = HoldLateCharge.ContactMeleeShare;

        internal const float StandOff = 15f; // m short of the target's near edge

        // Close-order blocks (ShieldWall, Square, Circle) advance formed and break into the charge only this close.
        internal const float ClosedChargeStartDistance = 10f; // m
        internal const float ClosedStandOff = 3f; // m

        internal static bool IsCloseOrder(Formation formation)
        {
            ArrangementOrder.ArrangementOrderEnum arrangement = formation.ArrangementOrder.OrderEnum;
            return arrangement == ArrangementOrder.ArrangementOrderEnum.ShieldWall
                || arrangement == ArrangementOrder.ArrangementOrderEnum.Square
                || arrangement == ArrangementOrder.ArrangementOrderEnum.Circle;
        }

        private const float FacingTurnCos = 0.985f; // ~10 degrees

        private const float MeleeCacheSeconds = 1f; // reading it walks the men

        private const float RecheckSeconds = 0.5f; // safety net

        private sealed class State
        {
            public volatile bool Advancing; // last order was a charge turned into the advance

            public bool Charging; // last order was a charge that went through (hysteresis)

            public float FarSince = float.NaN; // Charging: since when the gap is above ChargeKeepDistance (NaN while within)

            public volatile Formation Target;

            public BehaviorComponent Behavior; // the safety net re-applies only its order

            // Player formation: the order as given, re-applied by the safety net (no behavior re-issues it).
            public MovementOrder PlayerOrder;
            public bool HasPlayerOrder;

            // The charge actually let go (possibly re-aimed at a nearer formation), kept through the hysteresis.
            public MovementOrder ReleasedOrder;
            public bool HasReleasedOrder;

            public readonly CachedMovePoint Point = new CachedMovePoint();
            public Formation PointTarget;
            public Vec2 Facing = Vec2.Invalid;
            public float MeleeAt = float.NegativeInfinity;
            public float Melee; // main body only (MainBodyMeleeShare)
            public float MeleeAll;
            public int FarMen;
            public float RecheckAt;
            public float AdvanceSince;
            public bool HoldVsMountedLogged; // once per advance
        }

        // Keyed by reference: native Formation.GetHashCode throws for team-less simulation formations (order preview).
        private sealed class FormationReferenceComparer : System.Collections.Generic.IEqualityComparer<Formation>
        {
            public bool Equals(Formation x, Formation y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(Formation obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }

        private static readonly ConcurrentDictionary<Formation, State> States = new ConcurrentDictionary<Formation, State>(new FormationReferenceComparer());

        private static readonly Func<Formation, State> NewState = f => new State();

        // True while the safety net re-applies a player's charge (not a new order of the player's).
        [ThreadStatic] private static bool _reapplying;

        internal static void Clear()
        {
            States.Clear();
        }

        // Any thread. Its actual order is our Move (or AI riders' own formed approach, FormedApproach.Target).
        internal static bool IsAdvancingToCharge(Formation formation)
        {
            State state;
            return formation != null && ((States.TryGetValue(formation, out state) && state.Advancing)
                || FormedApproach.Target(formation) != null);
        }

        // Any thread: under a Charge / ChargeWithTarget order, or advancing to one.
        internal static bool IsChargeIntent(Formation formation)
        {
            if (formation == null)
            {
                return false;
            }
            OrderType order = formation.GetReadonlyMovementOrderReference().OrderType;
            return order == OrderType.Charge || order == OrderType.ChargeWithTarget || IsAdvancingToCharge(formation);
        }

        internal static Formation AdvanceTarget(Formation formation)
        {
            State state;
            if (formation != null && States.TryGetValue(formation, out state) && state.Advancing)
            {
                return state.Target;
            }
            return FormedApproach.Target(formation);
        }

        /// <summary>
        /// SetMovementOrder prefix: a far charge becomes the advance Move; any other order ends the charge intent. <paramref name="original"/>
        /// is the order as given, before a plain Charge was turned into a charge at the nearest large formation (kept for a player's re-applies).
        /// </summary>
        internal static void Apply(Formation formation, ref MovementOrder input, MovementOrder original)
        {
            if (formation.Team == null)
            {
                // A simulation formation (order preview): never a real order.
                return;
            }
            MovementOrder.MovementOrderEnum orderEnum = input.OrderEnum;
            bool charge = orderEnum == MovementOrder.MovementOrderEnum.Charge || orderEnum == MovementOrder.MovementOrderEnum.ChargeToTarget;
            State state;
            if (!charge)
            {
                if (States.TryGetValue(formation, out state))
                {
                    lock (state)
                    {
                        EndIntent(formation, state, orderEnum);
                    }
                }
                return;
            }
            Mission mission = Mission.Current;
            if (mission == null || !Applies(mission, formation))
            {
                if (States.TryGetValue(formation, out state))
                {
                    lock (state)
                    {
                        state.Advancing = false;
                        state.Charging = false;
                    }
                }
                return;
            }
            state = States.GetOrAdd(formation, NewState);
            // State is written by the AI thread and the main thread (player orders) and holds MovementOrder structs a concurrent writer
            // could tear: one lock per formation state, re-entrant, nothing in here waits on another lock.
            lock (state)
            {
                ApplyLocked(formation, ref input, original, state, mission);
            }
        }

        private static void ApplyLocked(Formation formation, ref MovementOrder input, MovementOrder original, State state, Mission mission)
        {
            MovementOrder.MovementOrderEnum orderEnum = input.OrderEnum;
            float now = mission.CurrentTime;
            Vec2 centre = RBMAI.Utilities.GetFormationCenter(formation);
            if (!centre.IsValid)
            {
                return;
            }

            bool playerFormation = !formation.IsAIControlled;
            if (playerFormation)
            {
                state.PlayerOrder = original;
                state.HasPlayerOrder = true;
            }
            Formation target = null;
            if (orderEnum == MovementOrder.MovementOrderEnum.ChargeToTarget)
            {
                if (input.TargetFormation != null && input.TargetFormation.CountOfUnits > 0)
                {
                    target = input.TargetFormation;
                }
                else
                {
                    // Target gone: fight whoever is nearest (a ChargeToTarget at an empty formation charges nothing); a player's order becomes the plain charge.
                    input = MovementOrder.MovementOrderCharge;
                    orderEnum = MovementOrder.MovementOrderEnum.Charge;
                    if (playerFormation)
                    {
                        state.PlayerOrder = input;
                    }
                }
            }
            float targetGap = float.MaxValue;
            Formation nearestSignificant = null;
            float nearestSignificantGap = float.MaxValue;
            Formation nearestFoot = null;
            float nearestFootGap = float.MaxValue;
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (enemy == null || enemy.CountOfUnits <= 0)
                    {
                        continue;
                    }
                    Vec2 c = RBMAI.Utilities.GetFormationCenter(enemy);
                    if (!c.IsValid)
                    {
                        continue;
                    }
                    float gap = EdgeGap(formation, centre, enemy, c);
                    if (enemy == target)
                    {
                        targetGap = gap;
                    }
                    if (!ThreatSignificance.IsSignificant(formation, enemy))
                    {
                        continue;
                    }
                    if (gap < nearestSignificantGap)
                    {
                        nearestSignificantGap = gap;
                        nearestSignificant = enemy;
                    }
                    FormationQuerySystem q = enemy.QuerySystem;
                    if (!q.IsCavalryFormation && !q.IsRangedCavalryFormation && gap < nearestFootGap)
                    {
                        nearestFootGap = gap;
                        nearestFoot = enemy;
                    }
                }
            }
            if (target == null)
            {
                // Enemy foot unless riders clearly outweigh it (ChargeTargetChoice).
                target = ChargeTargetChoice.Prefer(formation, nearestFoot, null) ?? nearestSignificant;
                if (target == nearestFoot)
                {
                    targetGap = nearestFootGap;
                }
                else if (target == nearestSignificant)
                {
                    targetGap = nearestSignificantGap;
                }
                else if (target != null)
                {
                    targetGap = EdgeGap(formation, centre, target, RBMAI.Utilities.GetFormationCenter(target));
                }
            }
            if (target == null)
            {
                // Nothing worth walking to: the charge as asked.
                return;
            }

            // The player's own "charge that formation" pick is never re-aimed.
            bool playerPick = playerFormation && original.OrderEnum == MovementOrder.MovementOrderEnum.ChargeToTarget && original.TargetFormation == target;
            float goGap = Math.Min(targetGap, nearestFootGap);
            bool closeOrder = IsCloseOrder(formation);
            float startDistance = closeOrder ? ClosedChargeStartDistance : ChargeStartDistance;
            string go = null;
            // What the released charge aims at: a nearer significant enemy FOOT formation within the start distance (in melee: any nearer one).
            Formation goTarget = target;
            // go = the rule's word (GoFoot / GoGap / GoMelee / "keep"); its log text is built only for the "charge go" line (GoText).
            // A mounted target chosen for its threat is not given up for a nearer foot formation that matters less.
            if (!playerPick && nearestFootGap <= startDistance && nearestFoot != target && nearestFootGap < targetGap
                && (!ChargeTargetChoice.IsMounted(target) || ChargeTargetChoice.Prefer(formation, nearestFoot, target) == nearestFoot))
            {
                go = GoFoot;
                goTarget = nearestFoot;
            }
            else if (targetGap <= startDistance)
            {
                go = GoGap;
            }
            // No "attacked" go: it only fired for cavalry riding near and broke the block up. A formed block should meet cavalry;
            // it lets go once the riders are in melee with it (the melee rule below).
            else if (state.Charging)
            {
                if (goGap <= ChargeKeepDistance)
                {
                    state.FarSince = float.NaN;
                    go = "keep";
                }
                else
                {
                    if (float.IsNaN(state.FarSince))
                    {
                        state.FarSince = now;
                    }
                    if (now - state.FarSince < ChargeKeepSeconds)
                    {
                        go = "keep";
                    }
                }
            }
            if (go == null)
            {
                if (now - state.MeleeAt >= MeleeCacheSeconds)
                {
                    state.MeleeAt = now;
                    // men fighting far ahead of the main body no longer let the whole formation go (log 2026-10-23 22:09: 10% stragglers
                    // released 230 men 150-200 m out, advance <-> go every few seconds)
                    state.MeleeAll = HoldLateCharge.MeleeShare(formation);
                    state.Melee = MainBodyMeleeShare(formation, out state.FarMen);
                }
                if (state.Melee >= MeleeShareGo)
                {
                    Formation candidate = (!playerPick && nearestFoot != null && nearestFoot != target && nearestFootGap < targetGap) ? nearestFoot : target;
                    Formation choice = playerPick ? target : ChargeTargetChoice.Prefer(formation, candidate, target);
                    if (choice != null && choice != target)
                    {
                        // A nearer foot formation, or riders that clearly outweigh the target.
                        go = GoMelee;
                        goTarget = choice;
                    }
                    else if (nearestSignificant != null && nearestSignificant != target && nearestSignificantGap < targetGap
                        && ChargeTargetChoice.IsMounted(nearestSignificant))
                    {
                        // Riders in the block that do not outweigh the far target: stay formed and keep walking at it.
                        if (state.Advancing && !state.HoldVsMountedLogged)
                        {
                            state.HoldVsMountedLogged = true;
                            Log(formation, now, "charge hold", target, "vsMounted " + ChargeTargetChoice.Describe(formation, nearestSignificant)
                                + " gap=" + Fmt0(nearestSignificantGap) + "m melee=" + state.Melee.ToString("0.00", CultureInfo.InvariantCulture)
                                + " target " + ChargeTargetChoice.Describe(formation, target) + " gap=" + Fmt0(targetGap) + "m");
                        }
                    }
                    else
                    {
                        go = GoMelee;
                    }
                }
            }
            if (go != null)
            {
                bool wasCharging = state.Charging;
                if (state.Advancing)
                {
                    state.Advancing = false;
                    Log(formation, now, "charge go", goTarget, GoText(go, nearestFoot, nearestFootGap, targetGap, closeOrder, state.Melee)
                        + (go == GoMelee ? " all=" + state.MeleeAll.ToString("0.00", CultureInfo.InvariantCulture) + " far=" + state.FarMen.ToString(CultureInfo.InvariantCulture) : "")
                        + " after " + Fmt0(now - state.AdvanceSince) + "s"
                        + (goTarget != target ? " reaimed" + (ChargeTargetChoice.IsMounted(goTarget) ? " vsMounted " + ChargeTargetChoice.Describe(formation, goTarget)
                            + " over " + ChargeTargetChoice.Describe(formation, target) : "") : ""));
                }
                if (go != "keep")
                {
                    state.FarSince = float.NaN;
                }
                state.Charging = true;
                if (playerFormation && wasCharging && _reapplying)
                {
                    // The player's charge is already running: re-applied only so the march can resume when the enemy got away; while it
                    // fights its current charge stays (no re-aim flipping mid-fight).
                    input = formation.GetReadonlyMovementOrderReference();
                    return;
                }
                if (go == "keep")
                {
                    Formation released = state.HasReleasedOrder ? state.ReleasedOrder.TargetFormation : null;
                    if (released != null && released != target && released.CountOfUnits > 0 && ChargeTargetChoice.IsMounted(released)
                        && ChargeTargetChoice.Prefer(formation, target, released) != released)
                    {
                        // The riders it went for no longer outweigh the asked target: back to that one, not after them.
                        input = MovementOrder.MovementOrderChargeToTarget(target);
                        state.ReleasedOrder = input;
                        return;
                    }
                    if (state.HasReleasedOrder && (state.ReleasedOrder.OrderEnum != MovementOrder.MovementOrderEnum.ChargeToTarget
                        || (state.ReleasedOrder.TargetFormation != null && state.ReleasedOrder.TargetFormation.CountOfUnits > 0)))
                    {
                        input = state.ReleasedOrder;
                    }
                    return;
                }
                if (goTarget != null && goTarget != input.TargetFormation)
                {
                    input = MovementOrder.MovementOrderChargeToTarget(goTarget);
                }
                state.ReleasedOrder = input;
                state.HasReleasedOrder = true;
                return;
            }

            // The advance.
            bool keepLost = state.Charging;
            state.Charging = false;
            state.FarSince = float.NaN;
            if (!SetPoint(formation, state, centre, target, now))
            {
                return;
            }
            if (!formation.IsAIControlled)
            {
                state.PlayerOrder = input;
                state.HasPlayerOrder = true;
            }
            input = MovementOrder.MovementOrderMove(state.Point.Point);
            FacingOrder facing = formation.FacingOrder;
            if (facing.OrderEnum != FacingOrder.FacingOrderEnum.LookAtDirection || facing.GetDirection(formation).DotProduct(state.Facing) < 0.9999f)
            {
                formation.SetFacingOrder(FacingOrder.FacingOrderLookAtDirection(state.Facing));
            }
            BehaviorComponent behavior = (formation.AI != null) ? formation.AI.ActiveBehavior : null;
            if (!state.Advancing)
            {
                state.AdvanceSince = now;
                state.HoldVsMountedLogged = false;
                Log(formation, now, "charge advance", target, TerrainCavalry.FormationName(formation) + " -> " + TerrainCavalry.FormationName(target)
                    + " gap=" + Fmt0(targetGap) + "m" + (behavior != null ? " by=" + behavior.GetType().Name : "")
                    + (keepLost ? " keepLost" : ""));
            }
            state.Target = target;
            state.Behavior = behavior;
            state.RecheckAt = now + RecheckSeconds;
            state.Advancing = true;
        }

        /// <summary>The guard's formations: not mounted, with men, in a field battle outside deployment; AI- and player-controlled alike.</summary>
        private static bool Applies(Mission mission, Formation formation)
        {
            if (formation.CountOfUnits <= 0 || formation.Team == null || !mission.IsFieldBattle
                || mission.Mode == MissionMode.Deployment)
            {
                return false;
            }
            FormationQuerySystem q = formation.QuerySystem;
            return q != null && !q.IsCavalryFormation && !q.IsRangedCavalryFormation;
        }

        /// <summary>A non-charge order: the intent ends (a facing still ours goes back to LookAtEnemy).</summary>
        private static void EndIntent(Formation formation, State state, MovementOrder.MovementOrderEnum orderEnum)
        {
            bool wasAdvancing = state.Advancing;
            state.Advancing = false;
            state.Charging = false;
            state.FarSince = float.NaN;
            state.HasPlayerOrder = false;
            state.HasReleasedOrder = false;
            if (!wasAdvancing)
            {
                return;
            }
            if (state.Facing.IsValid)
            {
                FacingOrder facing = formation.FacingOrder;
                if (facing.OrderEnum == FacingOrder.FacingOrderEnum.LookAtDirection && facing.GetDirection(formation).DotProduct(state.Facing) >= 0.9999f)
                {
                    formation.SetFacingOrder(FacingOrder.FacingOrderLookAtEnemy);
                }
            }
            Mission mission = Mission.Current;
            if (mission != null)
            {
                float now = mission.CurrentTime;
                Log(formation, now, "charge drop", state.Target, "order=" + orderEnum + " after " + Fmt0(now - state.AdvanceSince) + "s");
            }
        }

        /// <summary>The Move point and facing toward <paramref name="target"/> (see the class comment); false without a WorldPosition source.</summary>
        private static bool SetPoint(Formation formation, State state, Vec2 centre, Formation target, float now)
        {
            Vec2 targetCentre = RBMAI.Utilities.GetFormationCenter(target);
            if (!targetCentre.IsValid)
            {
                return false;
            }
            Vec2 toTarget = targetCentre - centre;
            float distance = toTarget.Normalize();
            if (distance < 0.01f)
            {
                return false;
            }
            if (!state.Facing.IsValid || state.Facing.DotProduct(toTarget) < FacingTurnCos || state.PointTarget != target)
            {
                state.Facing = toTarget;
            }
            Vec2 point = targetCentre - toTarget * (HalfExtent(target, toTarget) + (IsCloseOrder(formation) ? ClosedStandOff : StandOff));
            // a new target rebuilds at once and never keeps the old target's point
            if (state.PointTarget != target)
            {
                state.Point.Reset();
            }
            WorldPosition built;
            if (!state.Point.TryGet(point, now, target.CachedMedianPosition, formation.CachedMedianPosition, false, true, false, out built))
            {
                return false;
            }
            state.PointTarget = target;
            return true;
        }

        /// <summary>Half the footprint's extent along unit <paramref name="u"/>: |u.facing| x depth / 2 + |u.right| x width / 2.</summary>
        private static float HalfExtent(Formation formation, Vec2 u)
        {
            Vec2 facing = formation.Direction;
            float width = formation.Width;
            float depth = formation.Depth;
            if (!facing.IsValid || facing.LengthSquared < 0.01f)
            {
                return MathF.Max(width, depth) * 0.5f;
            }
            facing = facing.Normalized();
            float along = MathF.Abs(u.x * facing.x + u.y * facing.y);
            float across = MathF.Abs(u.x * facing.y - u.y * facing.x);
            return along * depth * 0.5f + across * width * 0.5f;
        }

        /// <summary>Edge gap (m) between two formations' footprints along the line between their centres, at least 0.</summary>
        internal static float EdgeGap(Formation a, Vec2 ca, Formation b, Vec2 cb)
        {
            Vec2 u = cb - ca;
            float distance = u.Normalize();
            if (distance < 0.01f)
            {
                return 0f;
            }
            return MathF.Max(0f, distance - HalfExtent(a, u) - HalfExtent(b, u));
        }

        private static void Log(Formation formation, float now, string what, Formation target, string why)
        {
            TerrainSpots.EnqueueBehavior(formation.Team, formation, now, "ChargeGuard", what, TerrainCavalry.FormationName(target), why);
        }

        private static string Fmt0(float value)
        {
            return value.ToString("0", CultureInfo.InvariantCulture);
        }

        // The release rules' words (the guard's `go`) and their "charge go" log text.
        private const string GoFoot = "foot";
        private const string GoGap = "gap";
        private const string GoMelee = "melee";

        private static string GoText(string go, Formation nearestFoot, float nearestFootGap, float targetGap, bool closeOrder, float melee)
        {
            switch (go)
            {
                case GoFoot:
                    return "foot " + TerrainCavalry.FormationName(nearestFoot) + " gap=" + Fmt0(nearestFootGap) + "m" + (closeOrder ? " closeOrder" : "");
                case GoGap:
                    return "gap=" + Fmt0(targetGap) + "m" + (closeOrder ? " closeOrder" : "");
                case GoMelee:
                    return "melee=main " + melee.ToString("0.00", CultureInfo.InvariantCulture);
                default:
                    return go;
            }
        }

        // RallyLogic's "far": more than this plus the depth from the men's median along the facing.
        private const float MainBodyMargin = 40f; // m
        private const float MainMeleeWindow = 6f; // s, FormationFightingInMelee's window

        [ThreadStatic] private static float[] _forward;
        [ThreadStatic] private static float[] _sorted;

        /// <summary>Melee share of the main body (stragglers far ahead or behind left out); farMen = those left out. Any thread: unit snapshot, managed hit times.</summary>
        internal static float MainBodyMeleeShare(Formation formation, out int farMen)
        {
            farMen = 0;
            RBMAI.Utilities.FormationUnitSnapshot snapshot = RBMAI.Utilities.GetFormationUnitSnapshot(formation, 0.5f);
            int count = snapshot.Positions.Length;
            Vec2 dir = formation.Direction;
            if (count == 0 || !dir.IsValid || dir.LengthSquared < 0.01f)
            {
                return HoldLateCharge.MeleeShare(formation);
            }
            dir = dir.Normalized();
            if (_forward == null || _forward.Length < count)
            {
                _forward = new float[count * 2];
                _sorted = new float[count * 2];
            }
            for (int i = 0; i < count; i++)
            {
                Vec2 p = snapshot.Positions[i];
                _forward[i] = p.x * dir.x + p.y * dir.y;
                _sorted[i] = _forward[i];
            }
            Array.Sort(_sorted, 0, count);
            float median = _sorted[count / 2];
            float limit = MainBodyMargin + formation.Depth;
            float time = MBCommon.GetTotalMissionTime();
            int main = 0;
            int fighting = 0;
            for (int i = 0; i < count; i++)
            {
                if (MathF.Abs(_forward[i] - median) > limit)
                {
                    farMen++;
                    continue;
                }
                main++;
                Agent agent = snapshot.Agents[i];
                if (agent != null && (time - agent.LastMeleeHitTime < MainMeleeWindow || time - agent.LastRecievedMeleeHitTime < MainMeleeWindow))
                {
                    fighting++;
                }
            }
            return (main > 0) ? (float)fighting / main : 0f;
        }

        /// <summary>Safety net: while a formation advances, re-applies its active behavior's own charge order every RecheckSeconds. AI thread.</summary>
        [HarmonyPatch(typeof(Formation), nameof(Formation.Tick))]
        internal static class TickRecheckPatch
        {
            private static void Postfix(Formation __instance)
            {
                try
                {
                    State state;
                    // Advancing; or a PLAYER's charge already let go, re-checked so the march resumes when the enemy got away.
                    if (__instance == null || !States.TryGetValue(__instance, out state)
                        || !(state.Advancing || (state.Charging && state.HasPlayerOrder)))
                    {
                        return;
                    }
                    Mission mission = Mission.Current;
                    if (mission == null)
                    {
                        return;
                    }
                    float now = mission.CurrentTime;
                    if (now < state.RecheckAt)
                    {
                        return;
                    }
                    state.RecheckAt = now + RecheckSeconds;
                    if (__instance.CountOfUnits <= 0)
                    {
                        state.Advancing = false;
                        return;
                    }
                    if (!__instance.IsAIControlled)
                    {
                        // The player's charge: re-apply it. A copy is taken under the state's lock (the main thread writes it); the order is given outside it.
                        bool hasPlayerOrder;
                        MovementOrder playerOrder;
                        lock (state)
                        {
                            hasPlayerOrder = state.HasPlayerOrder;
                            playerOrder = state.PlayerOrder;
                        }
                        if (hasPlayerOrder)
                        {
                            _reapplying = true;
                            DecisionTrace.Frame reapplyFrame = DecisionTrace.Enter(DecisionTrace.Scope.ChargeGuard, __instance);
                            try
                            {
                                __instance.SetMovementOrder(playerOrder);
                            }
                            finally
                            {
                                _reapplying = false;
                                DecisionTrace.Exit(reapplyFrame);
                            }
                        }
                        else
                        {
                            state.Advancing = false;
                        }
                        return;
                    }
                    // Handed over to the AI (delegated): its behaviors order for themselves.
                    state.HasPlayerOrder = false;
                    if (!state.Advancing)
                    {
                        return;
                    }
                    BehaviorComponent behavior = (__instance.AI != null) ? __instance.AI.ActiveBehavior : null;
                    if (behavior == null || behavior != state.Behavior)
                    {
                        // Another behavior took over: it orders for itself (FormationAI applies a changed order).
                        return;
                    }
                    MovementOrder order = behavior.CurrentOrder;
                    if (order.OrderEnum != MovementOrder.MovementOrderEnum.Charge && order.OrderEnum != MovementOrder.MovementOrderEnum.ChargeToTarget)
                    {
                        return;
                    }
                    // DecisionTrace (observation only): the re-applied charge is written by "chargeGuard".
                    DecisionTrace.Frame traceFrame = DecisionTrace.Enter(DecisionTrace.Scope.ChargeGuard, __instance);
                    try
                    {
                        __instance.SetMovementOrder(order);
                    }
                    finally
                    {
                        DecisionTrace.Exit(traceFrame);
                    }
                }
                catch (Exception e)
                {
                    // AI thread: never let an exception escape into the engine's tick (counted and logged, AiTickGuard).
                    AiTickGuard.Report("ChargeGuard safety net", e, null);
                }
            }
        }
    }
}
