using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// A terrain factor per tactic type and side, read from the terrain snapshot (BattleTerrain.Current): how much the ground
    /// favours each tactic. Applied by TacticSelection's Priority.Last GetTacticWeight postfix (terrainTacticsEnabled), so the
    /// final weight = composition fit x culture/commander/jitter x this factor; 1 before the publish or with the toggle off.
    /// Reads only the snapshot's raw measurements, never its map-type hint. A missing measurement makes its term 1 (logged n/a).
    /// Total = (product of the terms) ^ TerrainWeight, clamped to [TotalMin, TotalMax].
    /// Terms: room/exit/flanks = the splits; runup/clutter/steep = the wedge (runup = the LONGEST charge lane onto the enemy line);
    /// fire = the shooting tactics; adv = height; choke = the DEFENDER's holding tactics (and Hold the
    /// choke's own term); dead / water = defender holding; position = the defender's battle position; hidden = the reserve plug
    /// point; cavlanes / open / rough = the hedgehog.
    /// THREADING: Compute reads only the immutable snapshot and builds an immutable result. BattleTerrainLogic computes both
    /// sides once per field battle on the MAIN thread right after the snapshot publish and publishes them through Publish (two
    /// volatile writes); the AI thread reads them through ForSide (one volatile read, dictionary reads of an immutable object,
    /// no lock, no allocation). Cleared at mission end and in MissionStartReset.
    /// </summary>
    internal static class TerrainTacticFactors
    {

        private static volatile SideFactors _attacker;
        private static volatile SideFactors _defender;

        /// <summary>Null before the publish or outside a field battle. Any thread.</summary>
        public static SideFactors ForSide(BattleSideEnum side)
        {
            return (side == BattleSideEnum.Attacker) ? _attacker : (side == BattleSideEnum.Defender) ? _defender : null;
        }

        /// <summary>Main thread, at the terrain publish.</summary>
        internal static void Publish(SideFactors attacker, SideFactors defender)
        {
            _attacker = attacker;
            _defender = defender;
        }

        public static void Clear()
        {
            _attacker = null;
            _defender = null;
        }


        // Room to split: mid-field corridor width / wider frontage (the whole A-D corridor without a mid-field one); factor lerps Low -> High over RoomRatioLow -> High.
        public const float RoomRatioLow = 1f;
        public const float RoomRatioHigh = 3f;
        public const float RoomFactorLow = 0.75f;
        public const float RoomFactorHigh = 1.15f;

        // Splits: own exit width / own frontage below ExitSqueezeBelow = the army must squeeze out of its area.
        public const float ExitSqueezeBelow = 1f;
        public const float ExitSqueezeFactor = 0.85f;

        // Splits: own flank routes round the other army reachable: none / one / both.
        public const float FlanksNoneFactor = 0.85f;
        public const float FlanksOneFactor = 1f;
        public const float FlanksBothFactor = 1.05f;

        // Wedge: own riders' LONGEST run-up onto the enemy line (m, RunUpOntoEnemy.Max: cavalry charges down its best lane); lerps over RunUpShort -> RunUpLong.
        public const float RunUpShort = 40f;
        public const float RunUpLong = 150f;
        public const float RunUpFactorShort = 0.75f;
        public const float RunUpFactorLong = 1.15f;

        public const float ClutterAbove = 0.15f; // wedge: field-strip clutter share (no navmesh)
        public const float ClutterFactor = 0.85f;

        public const float SteepAbove = 0.25f; // wedge: field-strip share of walkable ground steeper than 15 deg
        public const float SteepFactor = 0.9f;

        // Shooting: own fields of fire (share of field points seen from at least one eye point on its line); lerp over FireLow -> FireHigh.
        public const float FireLow = 0.3f;
        public const float FireHigh = 0.9f;
        public const float FireFactorLow = 0.85f;
        public const float FireFactorHigh = 1.15f;

        // Height: adv = clamp((defender mean height - attacker mean height) / HeightAdvantageSpan, -1, 1); + = defender higher.
        public const float HeightAdvantageSpan = 20f;
        public const float DefenderHoldHeightStep = 0.2f; // defender holding tactics x(1 + step*adv)
        public const float DefenderAttackHeightStep = 0.2f; // defender FullScaleAttack (leaving its ground) x(1 - step*adv)
        public const float AttackerPushHeightStep = 0.15f; // attacker FullScaleAttack (push uphill) x(1 - step*max(adv, 0))

        // Choke: corridor ratio (as for room) below ChokeBelow favours the defender's holding tactics. No penalty on the attacker's splits (their room term covers narrow ground).
        public const float ChokeBelow = 1.5f;
        public const float ChokeHoldFactor = 1.1f;

        // Hold the choke (defender): its own "choke" term = a holdable choke (BattleTerrain.ChokeHold.Defender) ChokeQualifiesFactor, none ChokeNoneFactor.
        public const float ChokeQualifiesFactor = 1.3f;
        // ... up to this for a defender outnumbered 2.5x or more (BattleTerrainRead.OutnumberedK of the choke read)
        public const float ChokeQualifiesOutnumberedFactor = 1.6f;
        public const float ChokeNoneFactor = 0.5f;

        // Battle position (defender): holding tactics x lerp(1 -> PositionFactorMax) by the winner's score gain over its current line (0 -> PositionGainFull); Hold the choke too when the position IS its choke. The defender's FullScaleAttack gets 1 / (1 + PositionAttackShare x (that - 1)).
        public const float PositionGainFull = 0.5f;
        public const float PositionFactorMax = 1.3f;
        public const float PositionAttackShare = 0.5f;

        // Reserve plug (defender): its own "hidden" term = plug point hidden from the attacker's approach PlugHiddenFactor, the fallback straight behind the line PlugSeenFactor, none PlugNoneFactor (the reserve only follows its line); plus position and adv as the holding tactics.
        public const float PlugHiddenFactor = 1.2f;
        public const float PlugSeenFactor = 0.9f;
        public const float PlugNoneFactor = 0.85f;

        // Hedgehog: "cavlanes" = enemy's longest charge lane onto us (none: CavLaneFactorShort); "open" = our line's median clearance; "rough" = ground that already stops riders at our position (battle position's bad-ground share in front, x RoughChokeFactor in a choke; else the field strip's clutter).
        public const float CavLaneShort = 40f;
        public const float CavLaneLong = 150f;
        public const float CavLaneFactorShort = 0.85f;
        public const float CavLaneFactorLong = 1.2f;
        public const float OpenClearLow = 10f;
        public const float OpenClearHigh = 40f;
        public const float OpenFactorLow = 0.9f;
        public const float OpenFactorHigh = 1.1f;
        public const float RoughBadLow = 0.1f;
        public const float RoughBadHigh = 0.5f;
        public const float RoughClutterLow = 0.1f;
        public const float RoughClutterHigh = 0.35f;
        public const float RoughFactorMin = 0.7f;
        public const float RoughChokeFactor = 0.85f;

        public const float DeadGroundAbove = 0.4f; // the defender's unseen share of the field (1 - its fire share)
        public const float DeadGroundFactor = 0.9f;

        public const float DeepShareAtLeast = 0.05f; // water: armies on separate grounds, or the field strip's deep-water share at least this
        public const float WaterHoldFactor = 1.15f;
        public const float WaterPushFactor = 0.85f;

        /// <summary>Total = (product of the terms) ^ this. 2 squares every effect; the one knob for terrain's say as a whole.</summary>
        public const float TerrainWeight = 2f;

        public const float TotalMin = 0.35f; // clamp of the total, after TerrainWeight
        public const float TotalMax = 2.5f;

        // Every option TacticSelection registers.
        private static readonly Type[] TacticTypes =
        {
            typeof(TacticFullScaleAttack),
            typeof(RBMTacticEmbolon),
            typeof(RBMTacticAttackSplitSkirmishers),
            typeof(RBMTacticAttackSplitInfantry),
            typeof(RBMTacticAttackSplitArchers),
            typeof(TacticRangedHarrassmentOffensive),
            typeof(RBMTacticDefendSplitInfantry),
            typeof(RBMTacticDefendSplitArchers),
            typeof(RBMTacticHoldChoke),
            typeof(RBMTacticReservePlug),
            typeof(RBMTacticHedgehog),
            typeof(TacticDefensiveEngagement),
            typeof(TacticDefensiveLine),
        };

        /// <summary>Missing = its input was not measured (Value is then 1).</summary>
        internal struct Term
        {
            public readonly string Name;
            public readonly float Value;
            public readonly bool Missing;

            public Term(string name, float value, bool missing)
            {
                Name = name;
                Value = value;
                Missing = missing;
            }
        }

        /// <summary>One tactic type's factor for one side. Immutable.</summary>
        internal sealed class Factor
        {
            public readonly Type Tactic;
            public readonly float Raw; // product of the terms, before TerrainWeight
            public readonly float Total; // Raw ^ TerrainWeight, clamped: multiplies the option's weight
            public readonly IReadOnlyList<Term> Terms;

            internal Factor(Type tactic, float raw, float total, IReadOnlyList<Term> terms)
            {
                Tactic = tactic;
                Raw = raw;
                Total = total;
                Terms = terms;
            }

            /// <summary>"room=1.04,exit=0.85,flanks=n/a"; "-" with no term. ASCII.</summary>
            public string DescribeTerms()
            {
                if (Terms.Count == 0)
                {
                    return "-";
                }
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < Terms.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }
                    sb.Append(Terms[i].Name).Append('=').Append(Terms[i].Missing ? "n/a" : Fmt(Terms[i].Value));
                }
                return sb.ToString();
            }
        }

        /// <summary>All the factors of one side. Immutable.</summary>
        internal sealed class SideFactors
        {
            public readonly BattleSideEnum Side;
            public readonly string Inputs; // raw measurements used, ASCII (for the log)
            private readonly Dictionary<Type, Factor> _byType;

            internal SideFactors(BattleSideEnum side, string inputs, Dictionary<Type, Factor> byType)
            {
                Side = side;
                Inputs = inputs;
                _byType = byType;
            }

            /// <summary>Null for a type no factor was computed for.</summary>
            public Factor Get(Type tactic)
            {
                Factor f;
                return (tactic != null && _byType.TryGetValue(tactic, out f)) ? f : null;
            }

            /// <summary>1 for a type no factor was computed for.</summary>
            public float TotalFor(Type tactic)
            {
                Factor f = Get(tactic);
                return (f != null) ? f.Total : 1f;
            }
        }

        /// <summary>The raw measurements, read once per side. NaN / null = not measured.</summary>
        private sealed class Inputs
        {
            public BattleSideEnum Side;
            public float Corridor = float.NaN; // mid-field corridor / wider frontage, or the whole corridor's without a mid-field one
            public bool CorridorIsMid;
            public float ExitFront = float.NaN;
            public int FlanksReachable = -1; // -1 = no route measured either side
            public string FlanksText = "-";
            public float RunUp = float.NaN;
            public float Clutter = float.NaN;
            public float Steep = float.NaN;
            public float Fire = float.NaN;
            public float HeightDifference = float.NaN;
            public float DefenderDead = float.NaN;
            public bool? SameGround;
            public float Deep = float.NaN;
            public bool? ChokeHold; // defender: null = step failed, else whether a choke qualifies
            public string ChokeHoldId = "-";
            public float Outnumbered = float.NaN; // defender: attacker / defender strength at the choke read
            public float OutnumberedK;
            public float PositionGain = float.NaN; // defender: winner's score gain over the current line (NaN = not measured)
            public string PositionKind = "-";
            public bool PositionIsChoke;
            public bool? PlugHidden; // defender: null = no plug point measured
            public bool PlugNone; // defender: a winner but no plug point at all
            public int EnemyLanes = -1; // hedgehog: enemy charge lanes onto this side (-1 = not measured)
            public float EnemyLaneMax = float.NaN; // m
            public float ClearLine = float.NaN; // hedgehog: this side's line clearance, m
            public float PositionBadShare = float.NaN; // defender: battle position's bad-ground share in front
        }

        /// <summary>Null when the snapshot is null or the side is neither. Reads only the immutable snapshot; never throws for missing groups.</summary>
        public static SideFactors Compute(BattleTerrain terrain, BattleSideEnum side)
        {
            if (terrain == null || (side != BattleSideEnum.Attacker && side != BattleSideEnum.Defender))
            {
                return null;
            }
            Inputs inputs = Read(terrain, side);
            Dictionary<Type, Factor> byType = new Dictionary<Type, Factor>();
            foreach (Type type in TacticTypes)
            {
                List<Term> terms = new List<Term>();
                AddTerms(type, inputs, terms);
                float raw = 1f;
                foreach (Term term in terms)
                {
                    raw *= term.Value;
                }
                float total = Clamp((float)Math.Pow(raw, TerrainWeight), TotalMin, TotalMax);
                byType[type] = new Factor(type, raw, total, terms.AsReadOnly());
            }
            return new SideFactors(side, Describe(inputs), byType);
        }

        private static Inputs Read(BattleTerrain t, BattleSideEnum side)
        {
            Inputs i = new Inputs();
            i.Side = side;
            BattleTerrain.SideInfo own = (side == BattleSideEnum.Attacker) ? t.Attacker : t.Defender;

            float mid = (t.Open != null) ? t.Open.MidCorridorFront : float.NaN;
            float all = (t.Open != null) ? t.Open.CorridorFront : float.NaN;
            if (!float.IsNaN(mid))
            {
                i.Corridor = mid;
                i.CorridorIsMid = true;
            }
            else
            {
                i.Corridor = all;
            }

            if (own != null)
            {
                i.ExitFront = own.ExitFront;
                BattleTerrain.FlankRoute left = own.FlankLeft;
                BattleTerrain.FlankRoute right = own.FlankRight;
                if (left != null || right != null)
                {
                    i.FlanksReachable = ((left != null && left.Reachable) ? 1 : 0) + ((right != null && right.Reachable) ? 1 : 0);
                }
                i.FlanksText = Route(left) + "/" + Route(right);
                i.RunUp = (own.RunUpOntoEnemy != null) ? own.RunUpOntoEnemy.Max : float.NaN;
                i.Fire = own.FireAny;
                i.ClearLine = own.ClearLine;
            }

            // The enemy's charge lanes onto this side (the hedgehog).
            if (t.Cavalry != null && t.Cavalry.Error == null)
            {
                BattleTerrain.SideCavalry theirs = (side == BattleSideEnum.Attacker) ? t.Cavalry.Defender : t.Cavalry.Attacker;
                if (theirs != null)
                {
                    i.EnemyLanes = theirs.Lanes.Count;
                    float max = 0f;
                    foreach (BattleTerrain.ChargeLane lane in theirs.Lanes)
                    {
                        if (lane != null && !float.IsNaN(lane.Length))
                        {
                            max = Math.Max(max, lane.Length);
                        }
                    }
                    i.EnemyLaneMax = max;
                }
            }

            if (t.Field != null)
            {
                i.Clutter = t.Field.Clutter;
                i.Steep = t.Field.SteepShare;
                i.Deep = t.Field.DeepShare;
            }
            i.HeightDifference = t.HeightDifference;
            i.DefenderDead = (t.Defender != null) ? 1f - t.Defender.FireAny : float.NaN;
            i.SameGround = (t.Space != null) ? t.Space.SameGround : null;

            // The defender's choke hold (the worker's choice).
            if (side == BattleSideEnum.Defender && t.ChokeHold != null && t.ChokeHold.Error == null)
            {
                i.ChokeHold = t.ChokeHold.Defender != null;
                i.ChokeHoldId = (t.ChokeHold.Defender != null) ? t.ChokeHold.Defender.ChokeId : "none";
                i.Outnumbered = t.ChokeHold.Outnumbered;
                i.OutnumberedK = t.ChokeHold.OutnumberedK;
            }

            // The defender's battle position (the worker's winner).
            BattleTerrain.BattlePositionResult position = (t.BattlePosition != null && t.BattlePosition.Error == null) ? t.BattlePosition.Result : null;
            if (side == BattleSideEnum.Defender && position != null && position.Winner != null)
            {
                i.PositionGain = float.IsNaN(position.Gain) ? 0f : position.Gain;
                i.PositionKind = BattleTerrain.PositionKindName(position.Winner.Kind);
                i.PositionIsChoke = position.Winner.IsChoke;
                if (position.Winner.Plug.IsValid)
                {
                    i.PlugHidden = position.Winner.PlugHidden;
                }
                else
                {
                    i.PlugNone = true;
                }
                i.PositionBadShare = position.Winner.BadShare;
            }
            return i;
        }

        private static void AddTerms(Type type, Inputs i, List<Term> terms)
        {
            bool attacker = i.Side == BattleSideEnum.Attacker;
            bool split = type == typeof(RBMTacticAttackSplitInfantry) || type == typeof(RBMTacticAttackSplitArchers)
                || type == typeof(RBMTacticAttackSplitSkirmishers) || type == typeof(RBMTacticDefendSplitInfantry)
                || type == typeof(RBMTacticDefendSplitArchers);
            bool defenderHold = !attacker && (type == typeof(TacticDefensiveEngagement) || type == typeof(TacticDefensiveLine)
                || type == typeof(RBMTacticDefendSplitInfantry) || type == typeof(RBMTacticDefendSplitArchers));
            bool fullScale = type == typeof(TacticFullScaleAttack);
            // Hold the choke: its own choke term and the defender's height term.
            bool holdChoke = !attacker && type == typeof(RBMTacticHoldChoke);
            // Reserve plug: position + adv as the holding tactics, and its own hidden term.
            bool reservePlug = !attacker && type == typeof(RBMTacticReservePlug);
            bool shooting = type == typeof(TacticRangedHarrassmentOffensive) || type == typeof(RBMTacticAttackSplitArchers)
                || type == typeof(RBMTacticDefendSplitArchers);

            // ---- room to split ----
            if (split)
            {
                terms.Add(Usable(i.Corridor)
                    ? new Term("room", Lerp(i.Corridor, RoomRatioLow, RoomRatioHigh, RoomFactorLow, RoomFactorHigh), false)
                    : Missing("room"));
                terms.Add(Usable(i.ExitFront)
                    ? new Term("exit", (i.ExitFront < ExitSqueezeBelow) ? ExitSqueezeFactor : 1f, false)
                    : Missing("exit"));
                if (i.FlanksReachable < 0)
                {
                    terms.Add(Missing("flanks"));
                }
                else
                {
                    float f = (i.FlanksReachable == 0) ? FlanksNoneFactor : (i.FlanksReachable == 1) ? FlanksOneFactor : FlanksBothFactor;
                    terms.Add(new Term("flanks", f, false));
                }
            }

            // ---- cavalry ground (the wedge) ----
            if (type == typeof(RBMTacticEmbolon))
            {
                terms.Add(Usable(i.RunUp)
                    ? new Term("runup", Lerp(i.RunUp, RunUpShort, RunUpLong, RunUpFactorShort, RunUpFactorLong), false)
                    : Missing("runup"));
                terms.Add(Usable(i.Clutter)
                    ? new Term("clutter", (i.Clutter > ClutterAbove) ? ClutterFactor : 1f, false)
                    : Missing("clutter"));
                terms.Add(Usable(i.Steep)
                    ? new Term("steep", (i.Steep > SteepAbove) ? SteepFactor : 1f, false)
                    : Missing("steep"));
            }

            // ---- shooting ----
            if (shooting)
            {
                terms.Add(Usable(i.Fire)
                    ? new Term("fire", Lerp(i.Fire, FireLow, FireHigh, FireFactorLow, FireFactorHigh), false)
                    : Missing("fire"));
            }

            // ---- height ----
            // ---- the choke to hold (hold the choke only: a qualifying choke or none) ----
            if (holdChoke)
            {
                terms.Add(i.ChokeHold.HasValue
                    ? new Term("choke", i.ChokeHold.Value
                        ? ChokeQualifiesFactor + (ChokeQualifiesOutnumberedFactor - ChokeQualifiesFactor) * i.OutnumberedK
                        : ChokeNoneFactor, false)
                    : Missing("choke"));
            }

            // ---- the hidden reserve plug point (reserve plug only) ----
            if (reservePlug)
            {
                terms.Add(i.PlugHidden.HasValue
                    ? new Term("hidden", i.PlugHidden.Value ? PlugHiddenFactor : PlugSeenFactor, false)
                    : i.PlugNone ? new Term("hidden", PlugNoneFactor, false) : Missing("hidden"));
            }

            // ---- the hedgehog: long enemy charge lanes and open ground round us up, ground that already stops riders down ----
            if (type == typeof(RBMTacticHedgehog))
            {
                if (i.EnemyLanes < 0)
                {
                    terms.Add(Missing("cavlanes"));
                }
                else
                {
                    terms.Add(new Term("cavlanes", (i.EnemyLanes == 0) ? CavLaneFactorShort
                        : Lerp(i.EnemyLaneMax, CavLaneShort, CavLaneLong, CavLaneFactorShort, CavLaneFactorLong), false));
                }
                terms.Add(Usable(i.ClearLine)
                    ? new Term("open", Lerp(i.ClearLine, OpenClearLow, OpenClearHigh, OpenFactorLow, OpenFactorHigh), false)
                    : Missing("open"));
                if (Usable(i.PositionBadShare))
                {
                    float rough = Lerp(i.PositionBadShare, RoughBadLow, RoughBadHigh, 1f, RoughFactorMin);
                    terms.Add(new Term("rough", i.PositionIsChoke ? rough * RoughChokeFactor : rough, false));
                }
                else
                {
                    terms.Add(Usable(i.Clutter)
                        ? new Term("rough", Lerp(i.Clutter, RoughClutterLow, RoughClutterHigh, 1f, RoughFactorMin), false)
                        : Missing("rough"));
                }
            }

            bool heightTerm = defenderHold || fullScale || holdChoke || reservePlug;
            if (heightTerm)
            {
                if (!Usable(i.HeightDifference))
                {
                    terms.Add(Missing("adv"));
                }
                else
                {
                    float adv = Clamp(i.HeightDifference / HeightAdvantageSpan, -1f, 1f);
                    float f;
                    if (defenderHold || holdChoke || reservePlug)
                    {
                        f = 1f + DefenderHoldHeightStep * adv;
                    }
                    else if (!attacker)
                    {
                        f = 1f - DefenderAttackHeightStep * adv;
                    }
                    else
                    {
                        f = 1f - AttackerPushHeightStep * Math.Max(adv, 0f);
                    }
                    terms.Add(new Term("adv", f, false));
                }
            }

            // ---- the battle position (defender: its holding tactics, the choke hold when it is the choke; FullScaleAttack mildly against) ----
            bool positionHold = defenderHold || reservePlug || (holdChoke && i.PositionIsChoke);
            bool positionAttack = !attacker && fullScale;
            if (positionHold || positionAttack)
            {
                if (!Usable(i.PositionGain))
                {
                    terms.Add(Missing("position"));
                }
                else
                {
                    float hold = Lerp(i.PositionGain, 0f, PositionGainFull, 1f, PositionFactorMax);
                    terms.Add(new Term("position", positionHold ? hold : 1f / (1f + PositionAttackShare * (hold - 1f)), false));
                }
            }

            // ---- choke (defender holding only; the attacker's splits read narrow ground through room) ----
            if (defenderHold)
            {
                if (!Usable(i.Corridor))
                {
                    terms.Add(Missing("choke"));
                }
                else
                {
                    terms.Add(new Term("choke", (i.Corridor < ChokeBelow) ? ChokeHoldFactor : 1f, false));
                }
            }

            // ---- dead ground ----
            if (defenderHold)
            {
                terms.Add(Usable(i.DefenderDead)
                    ? new Term("dead", (i.DefenderDead > DeadGroundAbove) ? DeadGroundFactor : 1f, false)
                    : Missing("dead"));
            }

            // ---- water ----
            bool attackerPush = attacker && fullScale;
            if (defenderHold || attackerPush)
            {
                bool deepKnown = Usable(i.Deep);
                if (!i.SameGround.HasValue && !deepKnown)
                {
                    terms.Add(Missing("water"));
                }
                else
                {
                    bool water = (i.SameGround == false) || (deepKnown && i.Deep >= DeepShareAtLeast);
                    terms.Add(new Term("water", water ? (defenderHold ? WaterHoldFactor : WaterPushFactor) : 1f, false));
                }
            }
        }

        private static string Describe(Inputs i)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("corr=").Append(Fmt(i.Corridor)).Append(i.CorridorIsMid ? "(mid)" : "(all)");
            sb.Append(" exit=").Append(Fmt(i.ExitFront));
            sb.Append(" flanksLR=").Append(i.FlanksText);
            sb.Append(" runupMax=").Append(Fmt(i.RunUp));
            sb.Append(" clutter=").Append(Fmt(i.Clutter));
            sb.Append(" steep15=").Append(Fmt(i.Steep));
            sb.Append(" fire=").Append(Fmt(i.Fire));
            sb.Append(" hdiff=").Append(Fmt(i.HeightDifference));
            sb.Append(" deadDef=").Append(Fmt(i.DefenderDead));
            sb.Append(" sameGround=").Append(i.SameGround.HasValue ? (i.SameGround.Value ? "1" : "0") : "-");
            sb.Append(" deep=").Append(Fmt(i.Deep));
            if (i.Side == BattleSideEnum.Defender)
            {
                sb.Append(" chokeHold=").Append(!i.ChokeHold.HasValue ? "-" : i.ChokeHoldId);
                sb.Append(" outnumbered=").Append(Fmt(i.Outnumbered)).Append(" k=").Append(Fmt(i.OutnumberedK));
                sb.Append(" position=").Append(Usable(i.PositionGain) ? i.PositionKind + "+" + Fmt(i.PositionGain) : "-");
                sb.Append(" plug=").Append(!i.PlugHidden.HasValue ? (i.PlugNone ? "none" : "-") : i.PlugHidden.Value ? "hidden" : "seen");
            }
            sb.Append(" cavLanesOntoUs=").Append((i.EnemyLanes < 0) ? "-" : i.EnemyLanes.ToString(CultureInfo.InvariantCulture) + ":" + Fmt(i.EnemyLaneMax));
            sb.Append(" clearLine=").Append(Fmt(i.ClearLine));
            return sb.ToString();
        }

        private static string Route(BattleTerrain.FlankRoute route)
        {
            return (route == null) ? "-" : (route.Reachable ? "1" : "0");
        }

        private static Term Missing(string name)
        {
            return new Term(name, 1f, true);
        }

        /// <summary>NaN is missing; an infinite ratio (zero frontage) is still an ordering and clamps in Lerp.</summary>
        private static bool Usable(float value)
        {
            return !float.IsNaN(value);
        }

        private static float Lerp(float x, float x0, float x1, float y0, float y1)
        {
            float u = Clamp((x - x0) / (x1 - x0), 0f, 1f);
            return y0 + (y1 - y0) * u;
        }

        private static float Clamp(float value, float min, float max)
        {
            return (value < min) ? min : (value > max) ? max : value;
        }

        private static string Fmt(float value)
        {
            return (float.IsNaN(value) || float.IsInfinity(value)) ? "-" : value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
