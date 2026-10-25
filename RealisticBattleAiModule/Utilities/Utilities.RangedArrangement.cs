using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    public static partial class Utilities
    {
        // ---- Line, Loose or a LOOSE circle for RBM's ranged formations (user rule: depending on the situation) ----
        //
        // One shared choice, used ONLY at RBM's own spots (archer behaviors, RBM's postfixes on native BehaviorSkirmishLine /
        // BehaviorScreenedSkirmish, the Archers roles of the hedgehog / HoldChoke / HoldPosition). The game's own behaviors keep their
        // choices. ShieldWall and a TIGHT Square / Circle are never chosen for a ranged formation anywhere in RBM
        // (IsRangedForArrangement); this is the only RBM code that gives one a Circle, and only a LOOSE one.
        // No Square: native RectilinearSchiltronFormation ignores the width order and forms a solid block at its MAXIMUM rank count.
        //
        // AI-controlled foot ranged formations only; anything else gets the caller's preferred arrangement back untouched.
        //
        // Precedence (first match wins):
        //  0. ring:       the caller says it stands inside an infantry circle                          -> loose Circle
        //  0. surrounded: enemy formations (>= RangedSurroundMinMen, edge within RangedSurroundRange) in >= RangedSurroundEnterQuadrants
        //                 of the four quadrants round its facing (left at <= RangedSurroundExitQuadrants), AND no friendly infantry
        //                 (>= RangedCoverMinMen) within RangedCoverEnterGap (leaves when infantry is within RangedCoverExitGap) -> loose Circle
        //  1. heavyFire: UnderRangedAttackRatio >= RangedHeavyFire                                   -> Loose
        //  2. cavalry:   enemy MELEE cavalry (>= RangedCavalryMinMen) COMING IN: within RangedCavalryThreatRange closing >=
        //                RangedCavalryChargeSpeed, or within RangedCavalryCloseRange closing >= RangedCavalryClosingSpeed, or within
        //                RangedCavalryContactRange; kept RangedCavalryKeepSeconds. Gaps use RangedStableRadius (its Line size: Loose's
        //                doubled width moved the gap and flipped Line / Loose)                          -> Line
        //  3. fire:      UnderRangedAttackRatio >= RangedLooseUnderFire; held until below RangedLineAgainFire for RangedLineAgainSeconds -> Loose
        //  4. ground:    BattleTerrain.WoodsOrRoughShare of its rectangle >= RangedBrokenGroundEnter (kept >= RangedBrokenGroundExit;
        //                terrainTacticsEnabled + snapshot only, re-read every RangedGroundCheckSeconds) -> Loose
        //  5. post:      a fixed post sheltered by infantry (the caller says so)                       -> Line
        //  6. default:   the caller's preferred arrangement.
        // At most one change per RangedArrangementCooldown s while the formation still stands in the arrangement this chose last.
        // ENTERING the loose circle is exempt (urgent, own hysteresis); leaving it is not.
        //
        // The loose circle: SetArrangementOrder(Circle) (native OnApply resets UnitSpacing to the Circle's 2), then SetPositioning(unitSpacing:
        // RangedLooseShapeSpacing) and FormOrder Wide. Every call re-asserts the spacing and the Wide width order; the width order it
        // replaced is saved and put back on leaving (if still the Wide this set).
        //
        // THREADING: async AI thread: query-system reads, cached positions / velocities, the immutable BattleTerrain snapshot, Team.Side; no
        // engine spatial query, no WorldPosition, no MBRandom. Each formation's state is written only by its own behavior's tick.
        // Cleared in MissionStartReset (ClearRangedArrangement).

        internal const float RangedArrangementCooldown = 15f;

        // Under-fire Loose: entered at the first ratio, left below the second held for RangedLineAgainSeconds.
        internal const float RangedLooseUnderFire = 0.1f;
        internal const float RangedLineAgainFire = 0.02f;
        internal const float RangedLineAgainSeconds = 10f;

        // Loose even with enemy cavalry near.
        internal const float RangedHeavyFire = 0.25f;

        // Enemy melee cavalry (edge gap m, cached velocity toward us m/s) makes it close ranks: charging in, closing, or in contact.
        internal const float RangedCavalryThreatRange = 100f;
        internal const float RangedCavalryChargeSpeed = 4f;
        internal const float RangedCavalryCloseRange = 60f;
        internal const float RangedCavalryClosingSpeed = 1f;
        internal const float RangedCavalryContactRange = 20f;

        // Held after it last found cavalry coming in (a charge that pauses to wheel).
        internal const float RangedCavalryKeepSeconds = 10f;

        internal const int RangedCavalryMinMen = 5;

        // Broken ground (woods / no navmesh / steep) share of the formation's own rectangle that loosens it; exit is the hysteresis.
        internal const float RangedBrokenGroundEnter = 0.35f;
        internal const float RangedBrokenGroundExit = 0.25f;
        internal const float RangedGroundCheckSeconds = 3f;
        internal const float RangedGroundMinRect = 20f; // m

        // Native spacing: Line / Circle 2, Loose 6, Square / ShieldWall 0.
        internal const int RangedLooseShapeSpacing = 4;

        internal const float RangedSurroundRange = 150f; // edge gap, m (RBMBehaviorHedgehog's value)
        internal const int RangedSurroundMinMen = 5;
        internal const int RangedSurroundEnterQuadrants = 3;
        internal const int RangedSurroundExitQuadrants = 1; // 2 keeps it: hysteresis

        // Friendly infantry within the enter gap shelters it (no loose circle); within the exit gap ends one.
        internal const float RangedCoverEnterGap = 40f; // m
        internal const float RangedCoverExitGap = 30f; // m
        internal const int RangedCoverMinMen = 10;

        internal enum RangedArrangementReason
        {
            Default,
            Post,
            Ground,
            Fire,
            Cavalry,
            HeavyFire,
            Surrounded,
            Ring,
            Count
        }

        private static readonly string[] RangedReasonNames = { "default", "post", "ground", "fire", "cavalry", "heavyFire", "surrounded", "ring" };

        // Decision labels per shape and reason ("line(cavalry)" etc.), built once.
        private static readonly string[,] RangedLabels = BuildRangedLabels();

        private static string[,] BuildRangedLabels()
        {
            string[] shapes = { "looseCircle(", "line(", "loose(" };
            string[,] labels = new string[shapes.Length, RangedReasonNames.Length];
            for (int s = 0; s < shapes.Length; s++)
            {
                for (int r = 0; r < RangedReasonNames.Length; r++)
                {
                    labels[s, r] = shapes[s] + RangedReasonNames[r] + ")";
                }
            }
            return labels;
        }

        internal sealed class RangedArrangementState
        {
            public ArrangementOrder.ArrangementOrderEnum Applied = ArrangementOrder.ArrangementOrderEnum.Line;
            public bool HasApplied;
            public float ChangedAt = float.NegativeInfinity;
            public bool UnderFire;
            public float CalmSince = float.NaN;
            public bool BrokenGround;
            public float GroundAt = float.NegativeInfinity;
            public bool Surrounded; // latch with hysteresis
            public float CavalryUntil = float.NegativeInfinity; // mission time
            public FormOrder SavedForm; // replaced by the loose circle, put back on leaving
            public bool HasSavedForm;
            // Any thread.
            public volatile string Label;
        }

        internal static readonly ConcurrentDictionary<Formation, RangedArrangementState> rangedArrangementStates = new ConcurrentDictionary<Formation, RangedArrangementState>();

        // Mission totals by reason, Interlocked, for the AI log's AGENTS line.
        internal static readonly long[] RangedArrangementChanges = new long[(int)RangedArrangementReason.Count];

        internal static long RangedSpacingReasserts; // loose-circle spacings found reset and set again

        internal static void ClearRangedArrangement()
        {
            rangedArrangementStates.Clear();
            for (int i = 0; i < RangedArrangementChanges.Length; i++)
            {
                Interlocked.Exchange(ref RangedArrangementChanges[i], 0L);
            }
            Interlocked.Exchange(ref RangedSpacingReasserts, 0L);
        }

        // User rule: foot ranged at RBM spots stand about this many ranks deep (native Wide is ~3 ranks and too wide when Loose).
        internal const int RangedRanks = 4;
        internal const int RangedMinFiles = 8;
        internal const float RangedMaxWidth = 90f; // m

        // AI thread: custom width of ceil(men / RangedRanks) files (>= RangedMinFiles, <= RangedMaxWidth); the file count survives Line / Loose changes. False (nothing set) otherwise.
        internal static bool TrySetRangedWidth(Formation formation)
        {
            if (!IsRangedArrangementSubject(formation))
            {
                return false;
            }
            int files = Math.Max(RangedMinFiles, (int)Math.Ceiling(formation.CountOfUnits / (float)RangedRanks));
            float width = (files - 1) * (formation.UnitDiameter + formation.Interval) + formation.UnitDiameter;
            formation.SetFormOrder(FormOrder.FormOrderCustom(Math.Min(width, RangedMaxWidth)));
            return true;
        }

        internal static bool IsRangedArrangementSubject(Formation formation)
        {
            if (formation == null || formation.CountOfUnits <= 0 || !formation.IsAIControlled)
            {
                return false;
            }
            FormationQuerySystem q = formation.QuerySystem;
            if (q.IsRangedCavalryFormation || q.IsCavalryFormation || q.RangedCavalryUnitRatio > q.RangedUnitRatio)
            {
                return false;
            }
            return IsRangedForArrangement(formation);
        }

        /// <summary>
        /// Line, Loose or a loose Circle for an RBM ranged spot. <paramref name="preferred"/> = the caller's arrangement (Line or Loose),
        /// <paramref name="fixedPost"/> = holds a post sheltered by infantry, <paramref name="insideRing"/> = inside an infantry circle.
        /// Formations outside the rule get <paramref name="preferred"/> back. AI thread.
        /// </summary>
        internal static ArrangementOrder.ArrangementOrderEnum ChooseRangedArrangement(Formation formation, ArrangementOrder.ArrangementOrderEnum preferred, bool fixedPost = false, bool insideRing = false)
        {
            if (preferred != ArrangementOrder.ArrangementOrderEnum.Line && preferred != ArrangementOrder.ArrangementOrderEnum.Loose)
            {
                return preferred;
            }
            try
            {
                Mission mission = Mission.Current;
                if (mission == null || !IsRangedArrangementSubject(formation))
                {
                    return preferred;
                }
                float now = mission.CurrentTime;
                RangedArrangementState s = rangedArrangementStates.GetOrAdd(formation, _ => new RangedArrangementState());
                float fire = formation.QuerySystem.UnderRangedAttackRatio;
                if (fire >= RangedLooseUnderFire)
                {
                    s.UnderFire = true;
                    s.CalmSince = float.NaN;
                }
                else if (s.UnderFire)
                {
                    if (fire < RangedLineAgainFire)
                    {
                        if (float.IsNaN(s.CalmSince))
                        {
                            s.CalmSince = now;
                        }
                        else if (now - s.CalmSince >= RangedLineAgainSeconds)
                        {
                            s.UnderFire = false;
                            s.CalmSince = float.NaN;
                        }
                    }
                    else
                    {
                        s.CalmSince = float.NaN;
                    }
                }
                UpdateRangedBrokenGround(formation, s, now);
                UpdateRangedSurrounded(mission, formation, s);
                if (MeleeCavalryThreat(mission, formation))
                {
                    s.CavalryUntil = now + RangedCavalryKeepSeconds;
                }

                RangedArrangementReason reason;
                ArrangementOrder.ArrangementOrderEnum wanted;
                if (insideRing)
                {
                    reason = RangedArrangementReason.Ring;
                    wanted = ArrangementOrder.ArrangementOrderEnum.Circle;
                }
                else if (s.Surrounded)
                {
                    reason = RangedArrangementReason.Surrounded;
                    wanted = ArrangementOrder.ArrangementOrderEnum.Circle;
                }
                else if (fire >= RangedHeavyFire)
                {
                    reason = RangedArrangementReason.HeavyFire;
                    wanted = ArrangementOrder.ArrangementOrderEnum.Loose;
                }
                else if (now < s.CavalryUntil)
                {
                    reason = RangedArrangementReason.Cavalry;
                    wanted = ArrangementOrder.ArrangementOrderEnum.Line;
                }
                else if (s.UnderFire)
                {
                    reason = RangedArrangementReason.Fire;
                    wanted = ArrangementOrder.ArrangementOrderEnum.Loose;
                }
                else if (s.BrokenGround)
                {
                    reason = RangedArrangementReason.Ground;
                    wanted = ArrangementOrder.ArrangementOrderEnum.Loose;
                }
                else if (fixedPost)
                {
                    reason = RangedArrangementReason.Post;
                    wanted = ArrangementOrder.ArrangementOrderEnum.Line;
                }
                else
                {
                    reason = RangedArrangementReason.Default;
                    wanted = preferred;
                }

                bool enteringCircle = wanted == ArrangementOrder.ArrangementOrderEnum.Circle && s.Applied != ArrangementOrder.ArrangementOrderEnum.Circle;
                if (s.HasApplied && wanted != s.Applied && !enteringCircle && formation.ArrangementOrder.OrderEnum == s.Applied && now - s.ChangedAt < RangedArrangementCooldown)
                {
                    // Last choice still stands and is recent: keep it.
                    return s.Applied;
                }
                if (!s.HasApplied || wanted != s.Applied)
                {
                    if (s.HasApplied || wanted != preferred)
                    {
                        Interlocked.Increment(ref RangedArrangementChanges[(int)reason]);
                    }
                    s.Applied = wanted;
                    s.HasApplied = true;
                    s.ChangedAt = now;
                }
                int shape = wanted == ArrangementOrder.ArrangementOrderEnum.Circle ? 0
                    : wanted == ArrangementOrder.ArrangementOrderEnum.Line ? 1 : 2;
                s.Label = RangedLabels[shape, (int)reason];
                return wanted;
            }
            catch (Exception e)
            {
                // AI thread: never let an exception escape into the engine's tick (counted and logged, AiTickGuard).
                AiTickGuard.Report("ChooseRangedArrangement", e, null);
                return preferred;
            }
        }

        /// <summary>Sets ChooseRangedArrangement's answer on <paramref name="formation"/> and returns true; false (nothing set) when the rule does not apply. AI thread.</summary>
        internal static bool TryApplyRangedArrangement(Formation formation, ArrangementOrder.ArrangementOrderEnum preferred, bool fixedPost = false, bool insideRing = false)
        {
            if (!IsRangedArrangementSubject(formation))
            {
                return false;
            }
            ArrangementOrder.ArrangementOrderEnum chosen = ChooseRangedArrangement(formation, preferred, fixedPost, insideRing);
            if (chosen != ArrangementOrder.ArrangementOrderEnum.Line && chosen != ArrangementOrder.ArrangementOrderEnum.Loose
                && chosen != ArrangementOrder.ArrangementOrderEnum.Circle)
            {
                return false;
            }
            RangedArrangementState s;
            rangedArrangementStates.TryGetValue(formation, out s);
            if (chosen == ArrangementOrder.ArrangementOrderEnum.Circle)
            {
                ApplyLooseCircle(formation, s);
                return true;
            }
            if (formation.ArrangementOrder.OrderEnum != chosen)
            {
                // Out of a loose circle too: the new arrangement's OnApply puts its own spacing back.
                formation.SetArrangementOrder(chosen == ArrangementOrder.ArrangementOrderEnum.Line ? ArrangementOrder.ArrangementOrderLine : ArrangementOrder.ArrangementOrderLoose);
            }
            if (s != null && s.HasSavedForm)
            {
                // Left the loose circle: the width order it replaced, unless something else set one since.
                s.HasSavedForm = false;
                if (formation.FormOrder.OrderEnum == FormOrder.FormOrderEnum.Wide && s.SavedForm.OrderEnum != FormOrder.FormOrderEnum.Wide)
                {
                    formation.SetFormOrder(s.SavedForm);
                }
            }
            return true;
        }

        /// <summary>The loose circle: Circle, UnitSpacing RangedLooseShapeSpacing, FormOrder Wide. Re-asserts spacing and width order when reset. AI thread.</summary>
        private static void ApplyLooseCircle(Formation formation, RangedArrangementState s)
        {
            bool wasCircle = formation.ArrangementOrder.OrderEnum == ArrangementOrder.ArrangementOrderEnum.Circle;
            if (s != null && (!wasCircle || !s.HasSavedForm || formation.FormOrder.OrderEnum != FormOrder.FormOrderEnum.Wide))
            {
                // The width order to put back on leaving: the one on entering, or a behavior's latest, before the Wide below replaces it.
                s.SavedForm = formation.FormOrder;
                s.HasSavedForm = true;
            }
            if (!wasCircle)
            {
                formation.SetArrangementOrder(ArrangementOrder.ArrangementOrderCircle);
            }
            bool reform = false;
            if (formation.UnitSpacing != RangedLooseShapeSpacing)
            {
                if (wasCircle)
                {
                    Interlocked.Increment(ref RangedSpacingReasserts);
                }
                formation.SetPositioning(null, null, RangedLooseShapeSpacing);
                reform = true;
            }
            if (reform || formation.FormOrder.OrderEnum != FormOrder.FormOrderEnum.Wide)
            {
                // Re-forms the rings with the current interval (CircularFormation reads Interval / Distance).
                formation.SetFormOrder(FormOrder.FormOrderWide);
            }
        }

        /// <summary>Diameter (m) of the loose circle ApplyLooseCircle gives <paramref name="units"/> foot men (native's Wide / circumference maths). Pure maths, any thread. 0 without men.</summary>
        internal static float PredictLooseCircleDiameter(int units, float unitDiameter)
        {
            if (units <= 0)
            {
                return 0f;
            }
            float radial = Formation.InfantryInterval(RangedLooseShapeSpacing) + unitDiameter;
            float ringGap = Formation.InfantryDistance(RangedLooseShapeSpacing) + unitDiameter;
            if (radial <= 0f || ringGap <= 0f)
            {
                return 0f;
            }
            int maxFiles = TaleWorlds.Library.MathF.Max(TaleWorlds.Library.MathF.Round(TaleWorlds.Library.MathF.Sqrt(units / 16f)), 1) * 16;
            int rankCount = Math.Max(1, TaleWorlds.Library.MathF.Ceiling(units * 1f / maxFiles));
            int maxRanks = 0;
            int placed = 0;
            while (placed < units)
            {
                float radius = maxRanks * ringGap;
                placed += Math.Max(1, (int)((float)Math.PI * 2f * radius / radial));
                maxRanks++;
            }
            rankCount = Math.Min(Math.Max(maxRanks, 1), rankCount);
            float ringStep = (float)(Math.PI * 2.0 * ringGap);
            float circumference = Math.Max(0f, units * radial);
            float last;
            int count;
            do
            {
                last = circumference;
                circumference = Math.Max(0f, last - ringStep);
                count = 0;
                for (int i = 1; i <= rankCount; i++)
                {
                    count += (int)(Math.Max(0.0, circumference - (rankCount - i) * (Math.PI * 2.0 * ringGap)) / radial);
                }
            }
            while (count > units && last > 0f);
            return last / (float)Math.PI;
        }

        /// <summary>"arr=loose(fire)" for the map view's decision layer, or null when the rule never ran for it. Any thread.</summary>
        internal static string DescribeRangedArrangement(Formation formation)
        {
            RangedArrangementState s;
            if (formation == null || !rangedArrangementStates.TryGetValue(formation, out s))
            {
                return null;
            }
            string label = s.Label;
            return label == null ? null : "arr=" + label;
        }

        /// <summary>"default:0,post:2,...,ring:0,spacingFix:0" mission totals for the AI log.</summary>
        internal static string DescribeRangedArrangementChanges()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < (int)RangedArrangementReason.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append(RangedReasonNames[i]).Append(':').Append(Interlocked.Read(ref RangedArrangementChanges[i]));
            }
            sb.Append(",spacingFix:").Append(Interlocked.Read(ref RangedSpacingReasserts));
            return sb.ToString();
        }

        /// <summary>Broken-ground latch from the terrain snapshot, re-read every RangedGroundCheckSeconds; off without terrain tactics or a snapshot.</summary>
        private static void UpdateRangedBrokenGround(Formation formation, RangedArrangementState s, float now)
        {
            if (!RBMConfig.RBMConfig.terrainTacticsEnabled)
            {
                s.BrokenGround = false;
                return;
            }
            if (now - s.GroundAt < RangedGroundCheckSeconds)
            {
                return;
            }
            s.GroundAt = now;
            BattleTerrain terrain = BattleTerrain.Current;
            if (terrain == null)
            {
                s.BrokenGround = false;
                return;
            }
            Vec2 centre = GetFormationCenter(formation);
            Vec2 forward = formation.Direction;
            if (!centre.IsValid || !forward.IsValid || forward.LengthSquared < 0.01f)
            {
                return;
            }
            // Measured on its Line width (RangedStableRadius), not its current one: Loose roughly doubles the width and would flip the choice back.
            float width = Math.Max(RangedGroundMinRect, RangedStableRadius(formation) * 2f);
            float share = terrain.WoodsOrRoughShare(centre, forward, width, Math.Max(RangedGroundMinRect, formation.Depth));
            if (float.IsNaN(share))
            {
                return;
            }
            s.BrokenGround = s.BrokenGround ? share >= RangedBrokenGroundExit : share >= RangedBrokenGroundEnter;
        }

        /// <summary>The surrounded latch: enemies in enough quadrants (hysteresis) and no friendly infantry near (its own).</summary>
        private static void UpdateRangedSurrounded(Mission mission, Formation formation, RangedArrangementState s)
        {
            if (FriendlyInfantryCover(mission, formation, s.Surrounded ? RangedCoverExitGap : RangedCoverEnterGap))
            {
                s.Surrounded = false;
                return;
            }
            int quadrants = EnemyQuadrants(mission, formation, RangedSurroundMinMen, RangedSurroundRange);
            s.Surrounded = s.Surrounded ? quadrants > RangedSurroundExitQuadrants : quadrants >= RangedSurroundEnterQuadrants;
        }

        /// <summary>How many of the four quadrants round its facing hold an enemy formation of >= minMen within range (circle edge gap). Also the hedgehog's test.</summary>
        internal static int EnemyQuadrants(Mission mission, Formation formation, int minMen, float range)
        {
            Vec2 centre = GetFormationCenter(formation);
            Vec2 facing = formation.Direction;
            if (!centre.IsValid || !facing.IsValid || facing.LengthSquared < 0.01f || formation.Team == null)
            {
                return 0;
            }
            facing = facing.Normalized();
            Vec2 right = new Vec2(facing.y, -facing.x);
            bool front = false, rear = false, leftSide = false, rightSide = false;
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (enemy == null || enemy.CountOfUnits < minMen)
                    {
                        continue;
                    }
                    Vec2 c = GetFormationCenter(enemy);
                    if (!c.IsValid)
                    {
                        continue;
                    }
                    Vec2 rel = c - centre;
                    if (rel.Length - Math.Max(enemy.Width, enemy.Depth) * 0.5f > range)
                    {
                        continue;
                    }
                    float f = Vec2.DotProduct(rel, facing);
                    float r = Vec2.DotProduct(rel, right);
                    if (Math.Abs(f) >= Math.Abs(r))
                    {
                        if (f >= 0f)
                        {
                            front = true;
                        }
                        else
                        {
                            rear = true;
                        }
                    }
                    else if (r >= 0f)
                    {
                        rightSide = true;
                    }
                    else
                    {
                        leftSide = true;
                    }
                }
            }
            return (front ? 1 : 0) + (rear ? 1 : 0) + (leftSide ? 1 : 0) + (rightSide ? 1 : 0);
        }

        /// <summary>A friendly (same side) infantry formation of RangedCoverMinMen or more, not itself mostly ranged, within <paramref name="maxGap"/> m edge gap.</summary>
        private static bool FriendlyInfantryCover(Mission mission, Formation formation, float maxGap)
        {
            Vec2 centre = GetFormationCenter(formation);
            if (!centre.IsValid || formation.Team == null)
            {
                return false;
            }
            var side = formation.Team.Side;
            float ownRadius = RangedStableRadius(formation);
            foreach (Team team in mission.Teams)
            {
                if (team == null || team.Side != side)
                {
                    continue;
                }
                foreach (Formation friend in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (friend == null || friend == formation || friend.CountOfUnits < RangedCoverMinMen)
                    {
                        continue;
                    }
                    if (!friend.QuerySystem.IsInfantryFormation || IsRangedForArrangement(friend))
                    {
                        continue;
                    }
                    Vec2 c = GetFormationCenter(friend);
                    if (!c.IsValid)
                    {
                        continue;
                    }
                    float gap = c.Distance(centre) - Math.Max(friend.Width, friend.Depth) * 0.5f - ownRadius;
                    if (gap <= maxGap)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Half the size (m) the formation would have in Line with its current file count: an arrangement-independent radius for gap tests (its Width doubles when Loose). A loose circle keeps max(width, depth) / 2. Cached data only, any thread.</summary>
        internal static float RangedStableRadius(Formation formation)
        {
            if (formation == null)
            {
                return 0f;
            }
            float width = Math.Max(formation.Width, 0f);
            float depth = Math.Max(formation.Depth, 0f);
            if (formation.ArrangementOrder.OrderEnum == ArrangementOrder.ArrangementOrderEnum.Circle)
            {
                return Math.Max(width, depth) * 0.5f;
            }
            float diameter = Math.Max(formation.UnitDiameter, 0.1f);
            float step = diameter + Math.Max(formation.Interval, 0f);
            int files = Math.Max(1, (int)Math.Round((Math.Max(width, diameter) - diameter) / step) + 1);
            float lineWidth = (files - 1) * (diameter + Formation.InfantryInterval(2)) + diameter;
            return lineWidth * 0.5f;
        }

        /// <summary>Enemy melee cavalry (not horse archers) coming in: charging within RangedCavalryThreatRange, closing within RangedCavalryCloseRange, or within RangedCavalryContactRange (gaps by RangedStableRadius).</summary>
        private static bool MeleeCavalryThreat(Mission mission, Formation formation)
        {
            Vec2 centre = GetFormationCenter(formation);
            if (!centre.IsValid || formation.Team == null)
            {
                return false;
            }
            float ownRadius = RangedStableRadius(formation);
            foreach (Team team in mission.Teams)
            {
                if (team == null || !team.IsEnemyOf(formation.Team))
                {
                    continue;
                }
                foreach (Formation enemy in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (enemy == null || enemy.CountOfUnits < RangedCavalryMinMen)
                    {
                        continue;
                    }
                    FormationQuerySystem eq = enemy.QuerySystem;
                    if (!eq.IsCavalryFormation || eq.IsRangedCavalryFormation)
                    {
                        continue;
                    }
                    Vec2 c = GetFormationCenter(enemy);
                    if (!c.IsValid)
                    {
                        continue;
                    }
                    Vec2 toUs = centre - c;
                    float distance = toUs.Normalize();
                    float gap = distance - Math.Max(enemy.Width, enemy.Depth) * 0.5f - ownRadius;
                    if (gap <= RangedCavalryContactRange)
                    {
                        return true;
                    }
                    if (gap <= RangedCavalryThreatRange)
                    {
                        Vec2 velocity = enemy.CachedCurrentVelocity;
                        float closing = velocity.IsValid ? velocity.DotProduct(toUs) : 0f;
                        if (closing >= RangedCavalryChargeSpeed || (gap <= RangedCavalryCloseRange && closing >= RangedCavalryClosingSpeed))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }
    }
}
