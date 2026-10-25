using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.Library;

namespace RBMAI
{
    /// <summary>
    /// Battle position: where the defender should fight. BACKGROUND THREAD: reads only the grid, feature map, choke hold,
    /// archer spots and cavalry mask, no engine call. EvaluateBattlePosition is pure managed math over immutable
    /// PositionGround / PositionArmies, so the re-pick runs it again with the armies where they stand then.
    ///
    /// Candidates: the current line (always valid), the previous hold (re-pick), hill tops, crests and a point behind each
    /// crest, the choke hold, and a lattice round the defender. Others must be walkable, dry, not steep, off the map edge,
    /// have room for the infantry line, and be reached PosReachMargin s before the attacker (two bounded Dijkstra walk floods
    /// give every cell's walk at once). Facing: toward the attacker (a choke: through the gap).
    ///
    /// Score = weighted terms (first guesses, each logged): height over the ground in front, front narrower than the
    /// attacker's, covered line ends, archers' fire share, slow ground / open ground under fire in front, enemy-cavalry
    /// bad ground minus run-up, minus dead ground, minus march, minus being caught moving. The best wins only when it beats
    /// the reference (current line / previous hold) by PosMinGain. The winner gets an archer place (archer spot, else the
    /// highest cell behind), a cavalry reserve place and an infantry reserve plug point.
    ///
    /// Outnumbered (PositionWeights): the more the attacker outnumbers the defender, the more height, front, flanks,
    /// approach and cavalry ground weigh and the less march / caught do; the search reaches farther, the reach margin and
    /// PosMinGain shrink (a smaller margin never lets a cavalry-heavy attacker's riders arrive first).
    ///
    /// Plug point (RBMTacticReservePlug, PlacePositionPlug): a cell behind the line hidden from the attacker's approach
    /// (no sight line from its eye points), with room, dry, near the line ends' reach. Fallback chain, first hit wins:
    /// hidden, seen, relaxed, beside, nearest (PlugTier). None at all: no plug (the behavior follows its line).
    /// </summary>
    internal static partial class BattleTerrainRead
    {
        internal const float PosMaxDistance = 400f;
        internal const float PosLatticeStep = 25f;
        private const int PosMaxLattice = 900;
        internal const float PosReachMargin = 25f;
        private const float PosMaxSlope = 20f;
        internal const float PosMinRoom = 0.8f; // walkable width across the facing, x own infantry frontage
        private const float PosWidthCap = 2f; // width walk cap, x wider frontage, each side
        internal const float PosLineShare = 0.95f;
        internal const float PosLineMaxFront = 1.2f;
        internal const float PosMinGain = 0.15f;
        private const float PosReverseSlopeBack = 30f;
        private const float PosHeightNear = 50f;
        private const float PosHeightFar = 200f;
        private const float PosHeightStep = 25f;
        private const float PosHeightSpan = 15f;
        private static readonly float[] PosAcross = { -0.4f, 0f, 0.4f };
        private static readonly float[] PosFireDistances = { 30f, 60f, 90f, 120f, 150f, 180f };
        private const float PosApproachNear = 10f;
        private const float PosApproachDepth = 150f;
        private const float PosApproachStep = 8f;
        private const float PosSlowSteep = 15f;
        private const float PosSlowFull = 0.4f;
        private const float PosRunUpLimit = 250f;
        private const float PosRunUpSlope = 15f;
        private const float PosRunUpShort = 40f;
        private const float PosRunUpLong = 150f;
        private const float PosCoverSteep = 25f;
        private const int PosCoverCells = 3;
        private const float PosCaughtFull = 60f;
        private const float PosArcherFull = 0.2f;
        private const float PosCavalryFull = 0.3f;
        internal const float WHeight = 0.30f;
        internal const float WFront = 0.15f;
        internal const float WFlanks = 0.15f;
        internal const float WFire = 0.12f;
        internal const float WApproach = 0.12f;
        internal const float WCavalry = 0.10f;
        internal const float WDead = 0.08f;
        internal const float WMarch = 0.10f;
        internal const float WCaught = 0.10f;
        private const int PosRunnersUp = 3;
        private const float PosRunnerSeparation = 40f;
        private const float PosArcherSpotRange = 120f;
        private const float PosArcherMaxAhead = 5f;
        private const float PosArcherBehind = 35f;
        private const float PosArcherSearch = 12f;
        private const float PosArcherMinBehind = 25f;
        private const float PosReserveBehind = 60f;
        private const float PosReserveSearch = 25f;
        /// <summary>Defender's flood stops at this x PosMaxDistance; the attacker's at A-D distance + PosMaxDistance + slack.</summary>
        private const float PosDefenderFloodShare = 2.5f;
        private const float PosAttackerFloodSlack = 600f;
        private const float PosDefaultSpeed = 3f;

        // outnumbered defender: k = 0 at even odds (attacker / defender strength OutnumberedStart), 1 at OutnumberedFull
        internal const float OutnumberedStart = 1f;
        internal const float OutnumberedFull = 2.5f;
        private const float OutHeightBoost = 0.5f; // weight x (1 + boost x k)
        private const float OutFrontBoost = 1f;
        private const float OutFlanksBoost = 1f;
        private const float OutCavalryBoost = 0.5f;
        private const float OutApproachBoost = 0.5f;
        private const float OutMarchCut = 0.4f; // weight x (1 - cut x k)
        private const float OutCaughtCut = 0.4f;
        internal const float OutMaxDistance = 550f;
        internal const float OutReachMargin = 15f;
        private const float PosMinReachMargin = 10f;
        internal const float OutMinGain = 0.10f;
        /// <summary>From this attacker mounted share a margin below the base one must also beat the attacker's riders.</summary>
        internal const float OutCavalryShare = 0.35f;
        internal const float OutChokeMaxDistance = 450f;
        internal const float OutChokeReachMargin = 12f;

        /// <summary>Attacker / defender strength: power when both are read, else men; NaN when neither. source = from + "Power" | "Men".</summary>
        internal static float OutnumberRatio(float atkPower, float defPower, int atkMen, int defMen, string from, out string source)
        {
            if (!float.IsNaN(atkPower) && !float.IsNaN(defPower) && atkPower > 0f && defPower > 0f)
            {
                source = from + "Power";
                return atkPower / defPower;
            }
            if (atkMen > 0 && defMen > 0)
            {
                source = from + "Men";
                return (float)atkMen / defMen;
            }
            source = "-";
            return float.NaN;
        }

        internal static float OutnumberedK(float ratio)
        {
            return float.IsNaN(ratio) ? 0f : Clamp01((ratio - OutnumberedStart) / (OutnumberedFull - OutnumberedStart));
        }

        private static float FrameOutnumber(Frame f, out string source)
        {
            Inputs input = f.Input;
            if (input == null)
            {
                source = "-";
                return float.NaN;
            }
            return OutnumberRatio(input.AtkStrength, input.DefStrength, input.AtkStrengthMen, input.DefStrengthMen, input.StrengthSource, out source);
        }

        /// <summary>A relaxed reach margin: true when the defender also beats the attacker's riders there (only asked when cavalryCheck).</summary>
        private static bool BeatsRiders(bool cavalryCheck, float defenderSeconds, float attackerWalkSeconds, float attackerWalkSpeed, float attackerRideSpeed)
        {
            if (!cavalryCheck)
            {
                return true;
            }
            if (float.IsNaN(attackerRideSpeed) || attackerRideSpeed < 0.5f || float.IsNaN(attackerWalkSpeed) || attackerWalkSpeed <= 0f)
            {
                return false;
            }
            float riders = attackerWalkSeconds * attackerWalkSpeed / attackerRideSpeed;
            return float.IsNaN(riders) || defenderSeconds < riders;
        }

        /// <summary>One evaluation's weights and limits, by how outnumbered the defender is. Immutable.</summary>
        internal sealed class PositionWeights
        {
            public readonly float Ratio;
            public readonly float K;
            public readonly string Source;
            public readonly float Height, Front, Flanks, Fire, Approach, Cavalry, Dead, March, Caught;
            public readonly float MaxDistance, ReachMargin, MinGain;
            /// <summary>The attacker is cavalry-heavy: a margin below PosReachMargin also needs the defender there before its riders.</summary>
            public readonly bool CavalryCheck;

            public PositionWeights(float ratio, string source, float enemyMountedShare)
            {
                Ratio = ratio;
                K = OutnumberedK(ratio);
                Source = source ?? "-";
                Height = WHeight * (1f + OutHeightBoost * K);
                Front = WFront * (1f + OutFrontBoost * K);
                Flanks = WFlanks * (1f + OutFlanksBoost * K);
                Fire = WFire;
                Approach = WApproach * (1f + OutApproachBoost * K);
                Cavalry = WCavalry * (1f + OutCavalryBoost * K);
                Dead = WDead;
                March = WMarch * (1f - OutMarchCut * K);
                Caught = WCaught * (1f - OutCaughtCut * K);
                MaxDistance = PosMaxDistance + (OutMaxDistance - PosMaxDistance) * K;
                ReachMargin = Math.Max(PosMinReachMargin, PosReachMargin + (OutReachMargin - PosReachMargin) * K);
                MinGain = PosMinGain + (OutMinGain - PosMinGain) * K;
                CavalryCheck = enemyMountedShare >= OutCavalryShare;
            }

            public string Describe()
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("outnumbered=").Append(F2(Ratio)).Append(" k=").Append(F2(K)).Append('(').Append(Source).Append(')')
                  .Append(" w=h").Append(F2(Height)).Append(" f").Append(F2(Front)).Append(" fl").Append(F2(Flanks))
                  .Append(" cav").Append(F2(Cavalry)).Append(" ap").Append(F2(Approach)).Append(" m").Append(F2(March))
                  .Append(" c").Append(F2(Caught))
                  .Append(" maxDist=").Append(F0(MaxDistance)).Append(" reach=").Append(F0(ReachMargin)).Append('s')
                  .Append(" minGain=").Append(F2(MinGain)).Append(" cavReach=").Append(CavalryCheck ? 1 : 0);
                return sb.ToString();
            }
        }

        // infantry reserve plug
        internal const float PlugBackMin = 50f;
        internal const float PlugBackMax = 130f;
        private const float PlugLateral = 20f;
        private const float PlugMinStep = 5f;
        private const int PlugMaxCandidates = 900;
        internal const float PlugMinClear = 15f;
        private const float PlugMaxSlope = 15f;
        internal const float PlugReachSeconds = 25f; // to the farther line end, defender's pace
        private const float PlugHeightSpan = 5f;
        internal const float PlugFallbackBack = 70f;
        // fallback chain
        internal const float PlugRelaxedBackMin = 30f;
        internal const float PlugRelaxedBackMax = 150f;
        private const float PlugRelaxedLateral = 40f;
        private const float PlugRelaxedSlope = 20f;
        internal const float PlugRelaxedClear = 8f;
        internal const float PlugRelaxedEdge = 20f;
        internal const float PlugRelaxedReach = 45f;
        private const float PlugBesideBackMin = 5f;
        private const float PlugBesideInsideBack = 20f;
        private const float PlugBesideBackMax = 80f;
        private const float PlugBesideLateral = 100f;
        private const float PlugNearestSearch = 60f;
        private const float PlugNearestEdge = 10f;
        /// <summary>Eye points on the attacker's approach, m in front of the hold (only those nearer than the attacker).</summary>
        private static readonly float[] PlugEyeDistances = { 300f, 200f, 120f };
        private static readonly float[] PlugEyeAcross = { -0.33f, 0f, 0.33f };
        private static readonly string[] PlugRejectNames = { "ground", "wet", "edge", "steep", "room", "reach", "seen" };

        private static readonly string[] PosRejectNames = { "ground", "wet", "edge", "steep", "far", "room", "noRoute", "late" };
        private const int RejGround = 0, RejWet = 1, RejEdge = 2, RejSteep = 3, RejFar = 4, RejRoom = 5, RejNoRoute = 6, RejLate = 7;

        /// <summary>The ground a battle position is scored on. Immutable.</summary>
        internal sealed class PositionGround
        {
            public Grid Grid;
            /// <summary>Woods mask (null when that step failed).</summary>
            public bool[] Woods;
            /// <summary>The snapshot (features, choke hold, spots, cavalry mask).</summary>
            public BattleTerrain Terrain;
        }

        /// <summary>The armies a battle position is scored against. Immutable (a re-pick builds a new one).</summary>
        internal sealed class PositionArmies
        {
            public Vec2 Defender;
            public int DefenderSnap = -1;
            public float DefenderSpeed = PosDefaultSpeed;
            public Vec2 Attacker;
            public int AttackerSnap = -1;
            public float AttackerSpeed = PosDefaultSpeed;
            /// <summary>Defender's infantry (foot - ranged) and its frontage (4 deep at 1.1 m, min 20 m); the attacker's frontage.</summary>
            public int Infantry;
            public float InfantryFrontage = ChokeMinFrontage;
            public float AttackerFrontage = ChokeMinFrontage;
            public float RangedShare;
            public float EnemyMountedShare;
            /// <summary>The attacker's riders' pace (NaN without riders).</summary>
            public float AttackerRideSpeed = float.NaN;
            /// <summary>Attacker / defender strength (NaN = unread: even odds) and where it came from.</summary>
            public float Outnumbered = float.NaN;
            public string OutnumberedSource = "-";

            /// <summary>A copy with another strength ratio (re-pick, from the strengths now).</summary>
            internal PositionArmies WithOutnumbered(float ratio, string source)
            {
                PositionArmies copy = (PositionArmies)MemberwiseClone();
                copy.Outnumbered = ratio;
                copy.OutnumberedSource = source;
                return copy;
            }

            /// <summary>A copy with the army centres moved (re-pick).</summary>
            internal PositionArmies MovedTo(Grid grid, Vec2 defender, Vec2 attacker)
            {
                PositionArmies moved = (PositionArmies)MemberwiseClone();
                moved.Defender = defender;
                moved.Attacker = attacker;
                moved.DefenderSnap = Snap(grid, defender, SnapRadius);
                moved.AttackerSnap = Snap(grid, attacker, SnapRadius);
                return moved;
            }
        }

        /// <summary>One evaluation's working set (one thread).</summary>
        private sealed class PositionContext
        {
            public Grid Grid;
            public bool[] Woods;
            public BattleTerrain Terrain;
            public PositionArmies Armies;
            public float[] DefenderWalk;
            public float[] AttackerWalk;
            public float ArcherScale;
            public float CavalryScale;
            public bool HasCavalryMask;
            public PositionWeights W;
            public readonly int[] Rejects = new int[PosRejectNames.Length];
        }

        private static void ReadBattlePosition(Frame f)
        {
            BattleTerrain.BattlePositionInfo info = f.Terrain.BattlePosition;
            Side def = f.Def;
            Side atk = f.Atk;
            if (def.Snap < 0)
            {
                info.Why = "noDefenderSnap";
                return;
            }
            int infantry = Math.Max(0, def.Foot - Math.Min(def.Ranged, def.Foot));
            PositionArmies armies = new PositionArmies
            {
                Defender = def.C2,
                DefenderSnap = def.Snap,
                DefenderSpeed = Pace(def.WalkSpeed),
                Attacker = atk.C2,
                AttackerSnap = atk.Snap,
                AttackerSpeed = Pace(atk.WalkSpeed),
                Infantry = infantry,
                InfantryFrontage = Math.Max(ChokeMinFrontage, infantry / ChokeRanks * ChokeFileSpacing),
                AttackerFrontage = (float.IsNaN(atk.Frontage) || atk.Frontage < ChokeMinFrontage) ? ChokeMinFrontage : atk.Frontage,
                RangedShare = (def.Men > 0) ? (float)def.Ranged / def.Men : 0f,
                EnemyMountedShare = (atk.Men > 0) ? (float)atk.Mounted / atk.Men : 0f,
                AttackerRideSpeed = atk.MountedSpeed
            };
            string source;
            armies.Outnumbered = FrameOutnumber(f, out source);
            armies.OutnumberedSource = source;
            PositionGround ground = new PositionGround { Grid = f.Grid, Woods = f.WoodsMask, Terrain = f.Terrain };
            info.Ground = ground;
            info.Armies = armies;
            info.Result = EvaluateBattlePosition(ground, armies, Vec2.Invalid);
        }

        private static float Pace(float speed)
        {
            return (float.IsNaN(speed) || speed < 0.5f) ? PosDefaultSpeed : speed;
        }

        /// <summary>Scores the candidates and picks the battle position. Pure math over immutable inputs (publish task and re-pick task). previousHold (re-pick) is scored too and is the reference; skipChoke: the choke hold was bypassed, no candidate.</summary>
        internal static BattleTerrain.BattlePositionResult EvaluateBattlePosition(PositionGround ground, PositionArmies armies, Vec2 previousHold, bool skipChoke = false)
        {
            long startTicks = DateTime.UtcNow.Ticks;
            PositionWeights weights = new PositionWeights(armies.Outnumbered, armies.OutnumberedSource, armies.EnemyMountedShare);
            BattleTerrain.BattlePositionResult result = new BattleTerrain.BattlePositionResult
            {
                DefenderAt = armies.Defender,
                AttackerAt = armies.Attacker,
                Weights = weights
            };
            Grid grid = ground.Grid;
            if (armies.DefenderSnap < 0)
            {
                result.Why = "noDefenderSnap";
                result.Ms = (float)(DateTime.UtcNow.Ticks - startTicks) / TimeSpan.TicksPerMillisecond;
                return result;
            }

            long expanded = 0;
            PositionContext ctx = new PositionContext
            {
                Grid = grid,
                Woods = ground.Woods,
                Terrain = ground.Terrain,
                Armies = armies,
                ArcherScale = 0.25f + 0.75f * Clamp01(armies.RangedShare / PosArcherFull),
                CavalryScale = 0.25f + 0.75f * Clamp01(armies.EnemyMountedShare / PosCavalryFull),
                HasCavalryMask = ground.Terrain != null && ground.Terrain.Cavalry != null && ground.Terrain.Cavalry.BadMask != null,
                W = weights
            };
            float maxDistance = weights.MaxDistance;
            ctx.DefenderWalk = FloodDistances(grid, armies.DefenderSnap, maxDistance * PosDefenderFloodShare, ref expanded);
            ctx.AttackerWalk = (armies.AttackerSnap >= 0)
                ? FloodDistances(grid, armies.AttackerSnap, armies.Attacker.Distance(armies.Defender) + maxDistance + PosAttackerFloodSlack, ref expanded)
                : null;

            List<BattleTerrain.PositionCandidate> scored = new List<BattleTerrain.PositionCandidate>();
            int generated = 0;

            generated++;
            BattleTerrain.PositionCandidate current = ScoreAt(ctx, BattleTerrain.PositionKind.Current, "line", grid.CellCentre(armies.DefenderSnap), true);
            if (current != null)
            {
                scored.Add(current);
            }
            BattleTerrain.PositionCandidate previous = null;
            if (previousHold.IsValid)
            {
                generated++;
                previous = ScoreAt(ctx, BattleTerrain.PositionKind.Previous, "previous", previousHold, true);
                if (previous != null)
                {
                    scored.Add(previous);
                }
            }

            BattleTerrain terrain = ground.Terrain;
            if (terrain != null)
            {
                foreach (BattleTerrain.HillFeature hill in terrain.Hills)
                {
                    generated++;
                    Add(scored, ScoreAt(ctx, BattleTerrain.PositionKind.Hill, hill.Id, hill.Position, false));
                }
                foreach (BattleTerrain.CrestFeature crest in terrain.Crests)
                {
                    generated++;
                    Add(scored, ScoreAt(ctx, BattleTerrain.PositionKind.Crest, crest.Id, crest.Position, false));
                    Vec2 away = crest.Position - armies.Attacker;
                    if (away.LengthSquared > 1f)
                    {
                        generated++;
                        Add(scored, ScoreAt(ctx, BattleTerrain.PositionKind.ReverseSlope, crest.Id + "r", crest.Position + away.Normalized() * PosReverseSlopeBack, false));
                    }
                }
                BattleTerrain.ChokeHoldPlace choke = (terrain.ChokeHold != null && !skipChoke) ? terrain.ChokeHold.Defender : null;
                if (choke != null)
                {
                    generated++;
                    Add(scored, ScoreChoke(ctx, choke));
                }
            }

            Vec2 origin = grid.CellCentre(armies.DefenderSnap);
            // a wider circle keeps the lattice under PosMaxLattice with a coarser step (the cap cuts rows from one side)
            float latticeStep = Math.Max(PosLatticeStep, 1.05f * maxDistance * (float)Math.Sqrt(Math.PI / PosMaxLattice));
            int stride = Math.Max(1, (int)((latticeStep > PosLatticeStep) ? Math.Ceiling(latticeStep / grid.Step) : Math.Round(latticeStep / grid.Step)));
            int reach = (int)Math.Ceiling(maxDistance / grid.Step / stride) * stride;
            int ci = armies.DefenderSnap % grid.Nx;
            int cj = armies.DefenderSnap / grid.Nx;
            int lattice = 0;
            for (int j = cj - reach; j <= cj + reach && lattice < PosMaxLattice; j += stride)
            {
                if (j < 0 || j >= grid.Ny)
                {
                    continue;
                }
                for (int i = ci - reach; i <= ci + reach && lattice < PosMaxLattice; i += stride)
                {
                    if (i < 0 || i >= grid.Nx || (i == ci && j == cj))
                    {
                        continue;
                    }
                    Vec2 p = grid.CellCentre(j * grid.Nx + i);
                    if (p.Distance(origin) > maxDistance)
                    {
                        continue;
                    }
                    lattice++;
                    generated++;
                    Add(scored, ScoreAt(ctx, BattleTerrain.PositionKind.Lattice, "grid", p, false));
                }
            }

            result.Generated = generated;
            result.Scored = scored.Count;
            result.Current = current;
            result.Previous = previous;
            result.Expanded = expanded;
            result.Rejects = RejectText(ctx.Rejects);

            if (current == null)
            {
                result.Why = "noCurrentLine";
                result.Ms = (float)(DateTime.UtcNow.Ticks - startTicks) / TimeSpan.TicksPerMillisecond;
                return result;
            }

            scored.Sort((x, y) => y.Total.CompareTo(x.Total));
            BattleTerrain.PositionCandidate reference = previous ?? current;
            BattleTerrain.PositionCandidate best = scored[0];
            BattleTerrain.PositionCandidate winner = (best == reference || best.Total >= reference.Total + weights.MinGain) ? best : reference;
            if (winner.IsChoke && terrain != null && terrain.ChokeHold.Defender != null)
            {
                BattleTerrain.ChokeHoldPlace choke = terrain.ChokeHold.Defender;
                winner.Archer = choke.Archer;
                winner.ArcherSource = choke.ArcherSource;
                winner.ArcherGain = choke.ArcherGain;
                winner.Reserve = choke.Reserve;
            }
            else
            {
                PlacePositionArchers(ctx, winner);
                PlacePositionReserve(ctx, winner);
            }
            PlacePositionPlug(ctx, winner);
            if (previous != null && previous != winner)
            {
                // a re-pick whose move is blocked keeps the old hold and may only turn its facing: places for the old hold
                PlacePositionArchers(ctx, previous);
                PlacePositionReserve(ctx, previous);
                PlacePositionPlug(ctx, previous);
            }
            if (skipChoke && current != winner && current != previous)
            {
                // choke bypassed: a blocked move stands on the current line, never on the bypassed hold
                PlacePositionArchers(ctx, current);
                PlacePositionReserve(ctx, current);
                PlacePositionPlug(ctx, current);
            }
            List<BattleTerrain.PositionCandidate> runners = new List<BattleTerrain.PositionCandidate>();
            foreach (BattleTerrain.PositionCandidate c in scored)
            {
                if (runners.Count >= PosRunnersUp)
                {
                    break;
                }
                if (c == winner || c.Hold.Distance(winner.Hold) < PosRunnerSeparation)
                {
                    continue;
                }
                bool near = false;
                foreach (BattleTerrain.PositionCandidate r in runners)
                {
                    if (r.Hold.Distance(c.Hold) < PosRunnerSeparation)
                    {
                        near = true;
                        break;
                    }
                }
                if (!near)
                {
                    runners.Add(c);
                }
            }
            result.Winner = winner;
            result.RunnersUp = runners.AsReadOnly();
            result.Gain = (winner == current) ? 0f : winner.Total - current.Total;
            result.Ms = (float)(DateTime.UtcNow.Ticks - startTicks) / TimeSpan.TicksPerMillisecond;
            return result;
        }

        private static void Add(List<BattleTerrain.PositionCandidate> list, BattleTerrain.PositionCandidate c)
        {
            if (c != null)
            {
                list.Add(c);
            }
        }

        /// <summary>One candidate at p facing the attacker; null when it fails a test. mandatory (current line, previous hold): only needs a walkable cell.</summary>
        private static BattleTerrain.PositionCandidate ScoreAt(PositionContext ctx, BattleTerrain.PositionKind kind, string source, Vec2 p, bool mandatory)
        {
            Grid grid = ctx.Grid;
            PositionArmies armies = ctx.Armies;
            int k = grid.IndexOf(p);
            if (mandatory)
            {
                k = Snap(grid, p, SnapRadius);
                if (k < 0)
                {
                    return null;
                }
                p = grid.CellCentre(k);
            }
            else
            {
                if (!grid.IsWalkable(k))
                {
                    ctx.Rejects[RejGround]++;
                    return null;
                }
                if (grid.Wet[k])
                {
                    ctx.Rejects[RejWet]++;
                    return null;
                }
                if (grid.NearEdge(k))
                {
                    ctx.Rejects[RejEdge]++;
                    return null;
                }
                float slope = grid.Slopes[k];
                if (!float.IsNaN(slope) && slope > PosMaxSlope)
                {
                    ctx.Rejects[RejSteep]++;
                    return null;
                }
                if (p.Distance(armies.Defender) > ctx.W.MaxDistance)
                {
                    ctx.Rejects[RejFar]++;
                    return null;
                }
            }
            Vec2 toward = armies.Attacker - p;
            if (toward.LengthSquared < 1f)
            {
                toward = armies.Attacker - armies.Defender;
            }
            if (toward.LengthSquared < 1f)
            {
                toward = new Vec2(0f, 1f);
            }
            Vec2 facing = toward.Normalized();
            Vec2 lineDir = new Vec2(-facing.y, facing.x);

            float cap = PosWidthCap * Math.Max(armies.InfantryFrontage, armies.AttackerFrontage);
            BattleTerrain.End leftEnd, rightEnd;
            float left = Walk(grid, p, lineDir, cap, float.NaN, out leftEnd);
            float right = Walk(grid, p, -lineDir, cap, float.NaN, out rightEnd);
            float span = left + right;
            if (!mandatory && span < PosMinRoom * armies.InfantryFrontage)
            {
                ctx.Rejects[RejRoom]++;
                return null;
            }
            Vec2 hold = p;
            if (leftEnd != BattleTerrain.End.Max && rightEnd != BattleTerrain.End.Max && Math.Abs(left - right) > grid.Step)
            {
                Vec2 centre = p + lineDir * ((left - right) * 0.5f);
                int c = grid.IndexOf(centre);
                if (grid.IsWalkable(c) && !grid.Wet[c])
                {
                    hold = centre;
                }
            }
            float lineWidth = Math.Min(span * PosLineShare, armies.InfantryFrontage * PosLineMaxFront);

            BattleTerrain.PositionCandidate cand = new BattleTerrain.PositionCandidate
            {
                Kind = kind,
                Source = source,
                Hold = hold,
                Facing = facing,
                LineDirection = lineDir,
                Span = span,
                LeftEnd = leftEnd,
                RightEnd = rightEnd,
                LineWidth = lineWidth
            };
            if (!Reach(ctx, cand, mandatory, kind == BattleTerrain.PositionKind.Current))
            {
                return null;
            }
            ScoreTerms(ctx, cand, false);
            return cand;
        }

        /// <summary>The choke hold as a candidate: its hold point, gap facing, line and archer / reserve places.</summary>
        private static BattleTerrain.PositionCandidate ScoreChoke(PositionContext ctx, BattleTerrain.ChokeHoldPlace choke)
        {
            BattleTerrain.PositionCandidate cand = new BattleTerrain.PositionCandidate
            {
                Kind = BattleTerrain.PositionKind.Choke,
                Source = choke.ChokeId,
                Hold = choke.Hold,
                Facing = choke.Facing,
                LineDirection = new Vec2(-choke.Facing.y, choke.Facing.x),
                Span = choke.HoldSpan,
                LeftEnd = choke.HoldLeftEnd,
                RightEnd = choke.HoldRightEnd,
                LineWidth = choke.HoldWidth
            };
            if (!Reach(ctx, cand, false, false))
            {
                return null;
            }
            ScoreTerms(ctx, cand, choke.BothFlanksCovered);
            return cand;
        }

        /// <summary>Walk distance and times from the floods; false (rejected) when the defender cannot get there PosReachMargin s first.</summary>
        private static bool Reach(PositionContext ctx, BattleTerrain.PositionCandidate cand, bool mandatory, bool isCurrent)
        {
            int k = Snap(ctx.Grid, cand.Hold, 12f);
            float ours = (k >= 0) ? ctx.DefenderWalk[k] : float.PositiveInfinity;
            float theirs = (k >= 0 && ctx.AttackerWalk != null) ? ctx.AttackerWalk[k] : float.PositiveInfinity;
            if (isCurrent)
            {
                ours = 0f;
            }
            if (float.IsInfinity(ours))
            {
                if (!mandatory)
                {
                    ctx.Rejects[RejNoRoute]++;
                    return false;
                }
                ours = cand.Hold.Distance(ctx.Armies.Defender);
            }
            cand.Distance = ours;
            cand.DefenderSeconds = ours / ctx.Armies.DefenderSpeed;
            cand.AttackerSeconds = theirs / ctx.Armies.AttackerSpeed;
            if (!mandatory && cand.DefenderSeconds + PosReachMargin > cand.AttackerSeconds)
            {
                // an outnumbered defender takes a smaller margin, never one that lets the enemy's riders get there first
                PositionWeights w = ctx.W;
                bool relaxed = w.ReachMargin < PosReachMargin && cand.DefenderSeconds + w.ReachMargin <= cand.AttackerSeconds
                    && BeatsRiders(w.CavalryCheck, cand.DefenderSeconds, cand.AttackerSeconds, ctx.Armies.AttackerSpeed, ctx.Armies.AttackerRideSpeed);
                if (!relaxed)
                {
                    ctx.Rejects[RejLate]++;
                    return false;
                }
            }
            return true;
        }

        private static void ScoreTerms(PositionContext ctx, BattleTerrain.PositionCandidate cand, bool bothCovered)
        {
            Grid grid = ctx.Grid;
            PositionArmies armies = ctx.Armies;
            Vec2 hold = cand.Hold;
            Vec2 facing = cand.Facing;
            Vec2 lineDir = cand.LineDirection;
            float lineWidth = float.IsNaN(cand.LineWidth) ? armies.InfantryFrontage : cand.LineWidth;
            bool choke = cand.IsChoke;

            float holdHeight = grid.SampleHeight(hold);
            double sum = 0.0;
            int count = 0;
            for (float d = PosHeightNear; d <= PosHeightFar + 0.1f; d += PosHeightStep)
            {
                foreach (float o in PosAcross)
                {
                    Vec2 q = hold + facing * d + lineDir * (o * lineWidth);
                    if (grid.IndexOf(q) < 0)
                    {
                        continue;
                    }
                    float h = grid.SampleHeight(q);
                    if (IsPlausible(h))
                    {
                        sum += h;
                        count++;
                    }
                }
            }
            float adv = (IsPlausible(holdHeight) && count > 0) ? holdHeight - (float)(sum / count) : float.NaN;
            cand.HeightAdvantage = adv;
            cand.TermHeight = float.IsNaN(adv) ? 0f : Clamp(adv / PosHeightSpan, -1f, 1f);

            float span = float.IsNaN(cand.Span) ? lineWidth : cand.Span;
            float atkFront = Math.Max(1f, armies.AttackerFrontage);
            float narrow = Clamp01((atkFront - span) / atkFront);
            float shortfall = (!choke && span < armies.InfantryFrontage) ? (armies.InfantryFrontage - span) / armies.InfantryFrontage : 0f;
            cand.TermFront = narrow - 2f * shortfall;

            string leftCover = "choke";
            string rightCover = "choke";
            bool leftCovered = bothCovered || FlankCovered(ctx, hold, lineDir, lineWidth, out leftCover);
            bool rightCovered = bothCovered || FlankCovered(ctx, hold, -lineDir, lineWidth, out rightCover);
            cand.LeftCovered = leftCovered;
            cand.RightCovered = rightCovered;
            cand.LeftCover = leftCover;
            cand.RightCover = rightCover;
            cand.TermFlanks = (leftCovered ? 0.5f : 0f) + (rightCovered ? 0.5f : 0f);

            int targets = 0;
            int seen = 0;
            int terrainSeen = 0;
            if (IsPlausible(holdHeight))
            {
                Vec3 eye = new Vec3(hold, holdHeight + EyeHeight);
                foreach (float d in PosFireDistances)
                {
                    foreach (float o in PosAcross)
                    {
                        Vec2 q = hold + facing * d + lineDir * (o * lineWidth);
                        if (grid.IndexOf(q) < 0)
                        {
                            continue;
                        }
                        float h = grid.SampleHeight(q);
                        if (!IsPlausible(h))
                        {
                            continue;
                        }
                        targets++;
                        bool terrainClear;
                        if (SightLine(grid, ctx.Woods, eye, new Vec3(q, h + TargetHeight), true, out terrainClear))
                        {
                            seen++;
                        }
                        if (terrainClear)
                        {
                            terrainSeen++;
                        }
                    }
                }
            }
            float fire = (targets > 0) ? (float)seen / targets : float.NaN;
            float dead = (targets > 0) ? 1f - (float)terrainSeen / targets : float.NaN;
            cand.FireShare = fire;
            cand.TerrainSeenShare = (targets > 0) ? (float)terrainSeen / targets : float.NaN;
            cand.TermFire = float.IsNaN(fire) ? 0f : fire;
            cand.TermDead = float.IsNaN(dead) ? 0f : dead;

            int cells = 0;
            int slow = 0;
            int bad = 0;
            float half = lineWidth * 0.5f;
            for (float d = PosApproachNear; d <= PosApproachDepth; d += PosApproachStep)
            {
                for (float o = -half; o <= half + 0.1f; o += PosApproachStep)
                {
                    Vec2 q = hold + facing * d + lineDir * o;
                    int k = grid.IndexOf(q);
                    if (k < 0 || grid.Cells[k] == Cell.Out)
                    {
                        continue;
                    }
                    cells++;
                    bool isSlow = grid.Cells[k] != Cell.Open || grid.Wet[k] || (grid.Slopes[k] > PosSlowSteep)
                        || (ctx.Woods != null && ctx.Woods[k]);
                    if (isSlow)
                    {
                        slow++;
                    }
                    if (ctx.HasCavalryMask ? ctx.Terrain.Cavalry.IsBadGround(q) : isSlow)
                    {
                        bad++;
                    }
                }
            }
            float slowShare = (cells > 0) ? (float)slow / cells : 0f;
            float badShare = (cells > 0) ? (float)bad / cells : 0f;
            cand.SlowShare = slowShare;
            cand.BadShare = badShare;
            cand.TermApproach = 0.6f * Clamp01(slowShare / PosSlowFull) + 0.4f * (1f - slowShare) * cand.TermFire;

            double runUp = 0.0;
            int runs = 0;
            foreach (float o in new[] { -0.33f, 0f, 0.33f })
            {
                BattleTerrain.End end;
                runUp += Walk(grid, hold + lineDir * (o * lineWidth) + facing * grid.Step, facing, PosRunUpLimit, PosRunUpSlope, out end);
                runs++;
            }
            float meanRunUp = (float)(runUp / runs);
            cand.RunUp = meanRunUp;
            float runTerm = Clamp01((meanRunUp - PosRunUpShort) / (PosRunUpLong - PosRunUpShort));
            cand.TermCavalry = 0.5f * Clamp01(badShare / PosSlowFull) - 0.5f * runTerm;

            cand.TermMarch = Clamp01(cand.Distance / PosMaxDistance);
            float margin = cand.AttackerSeconds - cand.DefenderSeconds;
            cand.TermCaught = (float.IsInfinity(margin) || float.IsNaN(margin)) ? 0f
                : Clamp01((PosCaughtFull - margin) / (PosCaughtFull - PosReachMargin));

            PositionWeights w = ctx.W;
            cand.Total = w.Height * cand.TermHeight
                + w.Front * cand.TermFront
                + w.Flanks * cand.TermFlanks
                + w.Fire * ctx.ArcherScale * cand.TermFire
                + w.Approach * cand.TermApproach
                + w.Cavalry * ctx.CavalryScale * cand.TermCavalry
                - w.Dead * cand.TermDead
                - w.March * cand.TermMarch
                - w.Caught * cand.TermCaught;
        }

        /// <summary>One line end covered: water / no navmesh within one line width of the end, or at least PosCoverCells cells of woods, steep or bad ground. The map edge does not cover.</summary>
        private static bool FlankCovered(PositionContext ctx, Vec2 hold, Vec2 dir, float lineWidth, out string cover)
        {
            Grid grid = ctx.Grid;
            float half = lineWidth * 0.5f;
            float reach = half + lineWidth;
            BattleTerrain.End end;
            float distance = Walk(grid, hold, dir, reach, float.NaN, out end);
            if ((end == BattleTerrain.End.Water || end == BattleTerrain.End.Blocked) && distance <= reach)
            {
                cover = (end == BattleTerrain.End.Water) ? "water" : "blocked";
                return true;
            }
            int woods = 0, steep = 0, rough = 0;
            float limit = Math.Min(reach, distance);
            for (float s = half; s <= limit; s += grid.Step)
            {
                Vec2 q = hold + dir * s;
                int k = grid.IndexOf(q);
                if (k < 0)
                {
                    break;
                }
                if (ctx.Woods != null && ctx.Woods[k])
                {
                    woods++;
                }
                else if (grid.Slopes[k] > PosCoverSteep)
                {
                    steep++;
                }
                else if (ctx.HasCavalryMask && ctx.Terrain.Cavalry.IsBadGround(q))
                {
                    rough++;
                }
            }
            if (woods + steep + rough >= PosCoverCells)
            {
                cover = (woods >= steep && woods >= rough) ? "woods" : (steep >= rough) ? "steep" : "rough";
                return true;
            }
            cover = (end == BattleTerrain.End.Edge) ? "edge" : "-";
            return false;
        }

        /// <summary>The winner's archer place; left invalid when none fits.</summary>
        private static void PlacePositionArchers(PositionContext ctx, BattleTerrain.PositionCandidate c)
        {
            Grid grid = ctx.Grid;
            float holdHeight = grid.HeightAt(c.Hold);
            BattleTerrain.ArcherSpot bestSpot = null;
            if (ctx.Terrain != null && ctx.Terrain.Spots != null && ctx.Terrain.Spots.Defender != null)
            {
                foreach (BattleTerrain.ArcherSpot spot in ctx.Terrain.Spots.Defender.Spots)
                {
                    Vec2 rel = spot.Position - c.Hold;
                    if (rel.Length > PosArcherSpotRange || Vec2.DotProduct(rel, c.Facing) > PosArcherMaxAhead)
                    {
                        continue;
                    }
                    if (bestSpot == null || spot.Score > bestSpot.Score)
                    {
                        bestSpot = spot;
                    }
                }
            }
            if (bestSpot != null)
            {
                c.Archer = bestSpot.Position;
                c.ArcherSource = bestSpot.Id;
                c.ArcherGain = IsPlausible(holdHeight) ? bestSpot.Height - holdHeight : float.NaN;
                return;
            }
            Vec2 centre = c.Hold - c.Facing * PosArcherBehind;
            int bestCell = -1;
            float bestHeight = float.NegativeInfinity;
            ForCellsWithin(grid, centre, PosArcherSearch, k =>
            {
                if (!grid.IsWalkable(k) || grid.Wet[k] || grid.NearEdge(k) || !IsPlausible(grid.Heights[k]))
                {
                    return;
                }
                float slope = grid.Slopes[k];
                if (float.IsNaN(slope) || slope > PosMaxSlope)
                {
                    return;
                }
                if (Vec2.DotProduct(grid.CellCentre(k) - c.Hold, c.Facing) > -PosArcherMinBehind)
                {
                    return;
                }
                if (grid.Heights[k] > bestHeight)
                {
                    bestHeight = grid.Heights[k];
                    bestCell = k;
                }
            });
            if (bestCell >= 0)
            {
                c.Archer = grid.CellCentre(bestCell);
                c.ArcherSource = "behind";
                c.ArcherGain = IsPlausible(holdHeight) ? bestHeight - holdHeight : float.NaN;
            }
        }

        /// <summary>The winner's cavalry reserve: the dry walkable cell nearest the point PosReserveBehind m behind its hold.</summary>
        private static void PlacePositionReserve(PositionContext ctx, BattleTerrain.PositionCandidate c)
        {
            Grid grid = ctx.Grid;
            Vec2 centre = c.Hold - c.Facing * PosReserveBehind;
            int bestCell = -1;
            float bestDistance = float.MaxValue;
            ForCellsWithin(grid, centre, PosReserveSearch, k =>
            {
                if (!grid.IsWalkable(k) || grid.Wet[k] || grid.NearEdge(k))
                {
                    return;
                }
                float d = grid.CellCentre(k).Distance(centre);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    bestCell = k;
                }
            });
            if (bestCell >= 0)
            {
                c.Reserve = grid.CellCentre(bestCell);
            }
        }

        /// <summary>One tier's rules of the plug search. Immutable.</summary>
        private sealed class PlugRules
        {
            public string Name;
            public float BackMin;
            public float BackMax;
            public float Lateral;
            /// <summary>Within the line's span a cell must be at least this far behind the hold.</summary>
            public float InsideBackMin;
            public float MaxSlope;
            public float MinClear;
            public float EdgeMargin;
            public float ReachSeconds;
        }

        private static readonly PlugRules PlugStandardRules = new PlugRules
        {
            Name = "standard", BackMin = PlugBackMin, BackMax = PlugBackMax, Lateral = PlugLateral, InsideBackMin = PlugBackMin,
            MaxSlope = PlugMaxSlope, MinClear = PlugMinClear, EdgeMargin = EdgeMargin, ReachSeconds = PlugReachSeconds
        };

        private static readonly PlugRules PlugRelaxedRules = new PlugRules
        {
            Name = "relaxed", BackMin = PlugRelaxedBackMin, BackMax = PlugRelaxedBackMax, Lateral = PlugRelaxedLateral, InsideBackMin = PlugRelaxedBackMin,
            MaxSlope = PlugRelaxedSlope, MinClear = PlugRelaxedClear, EdgeMargin = PlugRelaxedEdge, ReachSeconds = PlugRelaxedReach
        };

        private static readonly PlugRules PlugBesideRules = new PlugRules
        {
            Name = "beside", BackMin = PlugBesideBackMin, BackMax = PlugBesideBackMax, Lateral = PlugBesideLateral, InsideBackMin = PlugBesideInsideBack,
            MaxSlope = PlugRelaxedSlope, MinClear = PlugRelaxedClear, EdgeMargin = PlugRelaxedEdge, ReachSeconds = PlugRelaxedReach
        };

        /// <summary>The line a plug is placed behind: its hold, facing, line direction, ends, height and the defender's pace.</summary>
        private struct PlugLine
        {
            public Vec2 Hold;
            public Vec2 Facing;
            public Vec2 LineDir;
            public float HalfWidth;
            public Vec2 LeftEnd;
            public Vec2 RightEnd;
            public float HoldHeight;
            public float Pace;
        }

        /// <summary>One scan's counts and its best hidden / best seen cell (-1 = none).</summary>
        private sealed class PlugScan
        {
            public int Tested;
            /// <summary>Cells that passed every rule but the sight test.</summary>
            public int Passed;
            public int Hidden;
            public int BestHidden = -1;
            public float BestHiddenScore = float.NegativeInfinity;
            public int BestSeen = -1;
            public float BestSeenScore = float.NegativeInfinity;
            public int[] Rejects = new int[PlugRejectNames.Length];
        }

        /// <summary>A candidate's reserve plug point via the fallback chain hidden, seen, relaxed, beside, nearest. Plug stays invalid when no tier finds a cell. Pure: grid reads only.</summary>
        private static void PlacePositionPlug(PositionContext ctx, BattleTerrain.PositionCandidate c)
        {
            Grid grid = ctx.Grid;
            PositionArmies armies = ctx.Armies;
            PlugLine line = new PlugLine
            {
                Hold = c.Hold,
                Facing = c.Facing,
                LineDir = c.LineDirection
            };
            float lineWidth = float.IsNaN(c.LineWidth) ? armies.InfantryFrontage : c.LineWidth;
            line.HalfWidth = lineWidth * 0.5f;
            line.HoldHeight = grid.SampleHeight(line.Hold);
            line.Pace = Math.Max(0.5f, armies.DefenderSpeed);
            line.LeftEnd = line.Hold + line.LineDir * line.HalfWidth;
            line.RightEnd = line.Hold - line.LineDir * line.HalfWidth;

            // attacker's eyes: its centre and points on its way in to the hold
            List<Vec3> eyes = new List<Vec3>();
            float attackerDistance = armies.Attacker.IsValid ? armies.Attacker.Distance(line.Hold) : float.NaN;
            if (armies.Attacker.IsValid)
            {
                AddEye(grid, eyes, armies.Attacker);
            }
            foreach (float d in PlugEyeDistances)
            {
                if (!float.IsNaN(attackerDistance) && d >= attackerDistance - 20f)
                {
                    continue;
                }
                foreach (float o in PlugEyeAcross)
                {
                    AddEye(grid, eyes, line.Hold + line.Facing * d + line.LineDir * (o * armies.AttackerFrontage));
                }
            }
            c.PlugEyes = eyes.Count;

            StringBuilder tiers = new StringBuilder();
            PlugScan standard = ScanPlug(ctx, line, PlugStandardRules, eyes, tiers);
            c.PlugCandidates = standard.Tested;
            c.PlugHiddenCells = standard.Hidden;
            c.PlugRejects = PlugRejectText(standard.Rejects);

            string tier = null;
            int bestCell = -1;
            float bestScore = float.NaN;
            bool hidden = false;
            if (standard.BestHidden >= 0)
            {
                tier = "hidden";
                bestCell = standard.BestHidden;
                bestScore = standard.BestHiddenScore;
                hidden = true;
            }
            else if (standard.BestSeen >= 0)
            {
                tier = "seen";
                bestCell = standard.BestSeen;
                bestScore = standard.BestSeenScore;
            }
            else
            {
                foreach (PlugRules rules in new[] { PlugRelaxedRules, PlugBesideRules })
                {
                    PlugScan scan = ScanPlug(ctx, line, rules, eyes, tiers);
                    if (scan.BestHidden >= 0 || scan.BestSeen >= 0)
                    {
                        tier = rules.Name;
                        hidden = scan.BestHidden >= 0;
                        bestCell = hidden ? scan.BestHidden : scan.BestSeen;
                        bestScore = hidden ? scan.BestHiddenScore : scan.BestSeenScore;
                        break;
                    }
                }
            }
            if (bestCell < 0)
            {
                // last tier: nearest walkable dry cell to the point straight behind the middle, level with or behind the hold
                Vec2 centre = line.Hold - line.Facing * PlugFallbackBack;
                float bestDistance = float.MaxValue;
                int found = 0;
                ForCellsWithin(grid, centre, PlugNearestSearch, k =>
                {
                    if (!grid.IsWalkable(k) || grid.Wet[k] || grid.NearEdge(k, PlugNearestEdge))
                    {
                        return;
                    }
                    Vec2 cell = grid.CellCentre(k);
                    if (Vec2.DotProduct(cell - line.Hold, line.Facing) > 0f)
                    {
                        return;
                    }
                    found++;
                    float d = cell.Distance(centre);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        bestCell = k;
                    }
                });
                if (tiers.Length > 0)
                {
                    tiers.Append(' ');
                }
                tiers.Append("nearest:").Append(found.ToString(CultureInfo.InvariantCulture));
                if (bestCell >= 0)
                {
                    tier = "nearest";
                    bestScore = float.NaN;
                }
            }
            c.PlugTiers = tiers.ToString();
            if (bestCell < 0)
            {
                return;
            }
            Vec2 plug = grid.CellCentre(bestCell);
            c.Plug = plug;
            c.PlugTier = tier;
            c.PlugHidden = hidden;
            c.PlugScore = bestScore;
            c.PlugLineDistance = plug.Distance(line.Hold);
            c.PlugReach = Math.Max(plug.Distance(line.LeftEnd), plug.Distance(line.RightEnd)) / line.Pace;
            c.PlugDrop = (IsPlausible(line.HoldHeight) && IsPlausible(grid.Heights[bestCell])) ? line.HoldHeight - grid.Heights[bestCell] : float.NaN;
        }

        /// <summary>One tier's scan behind the line: counts and the best hidden / seen cell; appends "name:tested/passed/hidden" to tiers. Pure: grid reads only.</summary>
        private static PlugScan ScanPlug(PositionContext ctx, PlugLine line, PlugRules rules, List<Vec3> eyes, StringBuilder tiers)
        {
            Grid grid = ctx.Grid;
            PlugScan scan = new PlugScan();
            float lateral = line.HalfWidth + rules.Lateral;
            float area = Math.Max(1f, (rules.BackMax - rules.BackMin) * 2f * lateral);
            float step = Math.Max(Math.Max(grid.Step, PlugMinStep), (float)Math.Sqrt(area / PlugMaxCandidates));
            HashSet<int> seenCells = new HashSet<int>();
            for (float back = rules.BackMin; back <= rules.BackMax + 0.1f && scan.Tested < PlugMaxCandidates; back += step)
            {
                for (float o = -lateral; o <= lateral + 0.1f && scan.Tested < PlugMaxCandidates; o += step)
                {
                    if (back < rules.InsideBackMin && Math.Abs(o) <= line.HalfWidth)
                    {
                        continue;
                    }
                    Vec2 p = line.Hold - line.Facing * back + line.LineDir * o;
                    int k = grid.IndexOf(p);
                    if (k < 0 || !seenCells.Add(k))
                    {
                        continue;
                    }
                    scan.Tested++;
                    if (!grid.IsWalkable(k))
                    {
                        scan.Rejects[0]++;
                        continue;
                    }
                    if (grid.Wet[k])
                    {
                        scan.Rejects[1]++;
                        continue;
                    }
                    if (grid.NearEdge(k, rules.EdgeMargin))
                    {
                        scan.Rejects[2]++;
                        continue;
                    }
                    float slope = grid.Slopes[k];
                    if (float.IsNaN(slope) || slope > rules.MaxSlope || !IsPlausible(grid.Heights[k]))
                    {
                        scan.Rejects[3]++;
                        continue;
                    }
                    if (grid.Clearance[k] < rules.MinClear)
                    {
                        scan.Rejects[4]++;
                        continue;
                    }
                    Vec2 cell = grid.CellCentre(k);
                    float reach = Math.Max(cell.Distance(line.LeftEnd), cell.Distance(line.RightEnd)) / line.Pace;
                    if (reach > rules.ReachSeconds)
                    {
                        scan.Rejects[5]++;
                        continue;
                    }
                    scan.Passed++;
                    float distance = cell.Distance(line.Hold);
                    float above = IsPlausible(line.HoldHeight) ? grid.Heights[k] - line.HoldHeight : 0f;
                    float score = 1f - 0.5f * Clamp01((distance - rules.BackMin) / Math.Max(1f, rules.BackMax - rules.BackMin))
                        - 0.5f * Clamp01(above / PlugHeightSpan);
                    Vec3 target = new Vec3(cell, grid.Heights[k] + TargetHeight);
                    // no eye point proves nothing: counted as seen
                    bool seen = eyes.Count == 0;
                    foreach (Vec3 eye in eyes)
                    {
                        bool terrainClear;
                        if (SightLine(grid, ctx.Woods, eye, target, false, out terrainClear))
                        {
                            seen = true;
                            break;
                        }
                    }
                    if (seen)
                    {
                        scan.Rejects[6]++;
                        if (score > scan.BestSeenScore)
                        {
                            scan.BestSeenScore = score;
                            scan.BestSeen = k;
                        }
                        continue;
                    }
                    scan.Hidden++;
                    if (score > scan.BestHiddenScore)
                    {
                        scan.BestHiddenScore = score;
                        scan.BestHidden = k;
                    }
                }
            }
            if (tiers.Length > 0)
            {
                tiers.Append(' ');
            }
            tiers.Append(rules.Name).Append(':').Append(scan.Tested.ToString(CultureInfo.InvariantCulture))
                .Append('/').Append(scan.Passed.ToString(CultureInfo.InvariantCulture))
                .Append('/').Append(scan.Hidden.ToString(CultureInfo.InvariantCulture));
            return scan;
        }

        /// <summary>An eye point EyeHeight above the ground at p; skipped off the grid or without a plausible height.</summary>
        private static void AddEye(Grid grid, List<Vec3> eyes, Vec2 p)
        {
            if (grid.IndexOf(p) < 0)
            {
                return;
            }
            float h = grid.SampleHeight(p);
            if (IsPlausible(h))
            {
                eyes.Add(new Vec3(p, h + EyeHeight));
            }
        }

        private static string PlugRejectText(int[] rejects)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < rejects.Length; i++)
            {
                if (rejects[i] <= 0)
                {
                    continue;
                }
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }
                sb.Append(PlugRejectNames[i]).Append(':').Append(rejects[i].ToString(CultureInfo.InvariantCulture));
            }
            return (sb.Length > 0) ? sb.ToString() : "-";
        }

        /// <summary>Walk distance (m) from start to every walkable cell (8 neighbours, no corner cutting, as PathSearch), Dijkstra stopped at maxDistance; +infinity beyond it or with no route.</summary>
        private static float[] FloodDistances(Grid grid, int start, float maxDistance, ref long expanded)
        {
            int n = grid.Nx * grid.Ny;
            float[] distance = new float[n];
            for (int k = 0; k < n; k++)
            {
                distance[k] = float.PositiveInfinity;
            }
            if (start < 0 || !grid.IsWalkable(start))
            {
                return distance;
            }
            bool[] closed = new bool[n];
            FloodHeap heap = new FloodHeap();
            float step = grid.Step;
            float diagonal = step * 1.41421356f;
            int nx = grid.Nx;
            int ny = grid.Ny;
            distance[start] = 0f;
            heap.Push(start, 0f);
            while (heap.Count > 0)
            {
                float key;
                int k = heap.Pop(out key);
                if (closed[k] || key > distance[k])
                {
                    continue;
                }
                if (key > maxDistance)
                {
                    break;
                }
                closed[k] = true;
                expanded++;
                int i = k % nx;
                int j = k / nx;
                for (int dj = -1; dj <= 1; dj++)
                {
                    int nj = j + dj;
                    if (nj < 0 || nj >= ny)
                    {
                        continue;
                    }
                    for (int di = -1; di <= 1; di++)
                    {
                        int ni = i + di;
                        if ((di == 0 && dj == 0) || ni < 0 || ni >= nx)
                        {
                            continue;
                        }
                        int nk = nj * nx + ni;
                        if (closed[nk] || grid.Cells[nk] != Cell.Open)
                        {
                            continue;
                        }
                        bool diagonalMove = di != 0 && dj != 0;
                        if (diagonalMove && (grid.Cells[j * nx + ni] != Cell.Open || grid.Cells[nj * nx + i] != Cell.Open))
                        {
                            continue;
                        }
                        float nd = key + (diagonalMove ? diagonal : step);
                        if (nd < distance[nk])
                        {
                            distance[nk] = nd;
                            heap.Push(nk, nd);
                        }
                    }
                }
            }
            for (int k = 0; k < n; k++)
            {
                if (!closed[k])
                {
                    distance[k] = float.PositiveInfinity;
                }
            }
            return distance;
        }

        /// <summary>A binary min-heap of (cell, key) for FloodDistances (one flood, one thread).</summary>
        private sealed class FloodHeap
        {
            private int[] _cells = new int[4096];
            private float[] _keys = new float[4096];

            public int Count;

            public void Push(int cell, float key)
            {
                if (Count == _cells.Length)
                {
                    Array.Resize(ref _cells, Count * 2);
                    Array.Resize(ref _keys, Count * 2);
                }
                int c = Count++;
                while (c > 0)
                {
                    int parent = (c - 1) >> 1;
                    if (_keys[parent] <= key)
                    {
                        break;
                    }
                    _cells[c] = _cells[parent];
                    _keys[c] = _keys[parent];
                    c = parent;
                }
                _cells[c] = cell;
                _keys[c] = key;
            }

            public int Pop(out float key)
            {
                int top = _cells[0];
                key = _keys[0];
                int lastCell = _cells[--Count];
                float lastKey = _keys[Count];
                int c = 0;
                while (true)
                {
                    int child = 2 * c + 1;
                    if (child >= Count)
                    {
                        break;
                    }
                    if (child + 1 < Count && _keys[child + 1] < _keys[child])
                    {
                        child++;
                    }
                    if (_keys[child] >= lastKey)
                    {
                        break;
                    }
                    _cells[c] = _cells[child];
                    _keys[c] = _keys[child];
                    c = child;
                }
                if (Count > 0)
                {
                    _cells[c] = lastCell;
                    _keys[c] = lastKey;
                }
                return top;
            }
        }

        private static string RejectText(int[] rejects)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < rejects.Length; i++)
            {
                if (rejects[i] <= 0)
                {
                    continue;
                }
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }
                sb.Append(PosRejectNames[i]).Append(':').Append(rejects[i].ToString(CultureInfo.InvariantCulture));
            }
            return (sb.Length > 0) ? sb.ToString() : "-";
        }

        private static float Clamp01(float value)
        {
            return Clamp(value, 0f, 1f);
        }

        private static float Clamp(float value, float min, float max)
        {
            return float.IsNaN(value) ? min : (value < min) ? min : (value > max) ? max : value;
        }
    }
}
