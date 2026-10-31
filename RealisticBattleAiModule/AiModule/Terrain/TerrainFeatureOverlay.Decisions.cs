using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RBMAI
{
    /// <summary>
    /// The map view's decision layer (Ctrl+Shift+D, 2nd and 3rd press): what each AI formation is choosing and why.
    /// - Per formation, at its cached average position: side, class, index, role in the tactic, the active behavior and the
    ///   best two others scored as FormationAI.FindBestBehavior compares (GetAIWeight x WeightFactor, keep bonus noted as
    ///   "x1.6"); a recently changed behavior is pale yellow with "*"; player formations get "player" and the order kind.
    ///   FindBestBehavior's NavmeshlessTargetPositionPenalty re-check is left out (it computes order positions).
    /// - Its order kind and a dotted line to the cached Formation.OrderPosition; role (the tactic's Formation fields,
    ///   reflected once per tactic type); claim (TerrainSpots spot / TerrainCavalry hold, thread-safe snapshots).
    /// - Per AI team: tactic and its options ranked from TacticWeightProbe.Rank (same numbers as the battle stats panel).
    ///
    /// SAFE READS ONLY: cached formation state, FormationAI's behavior list and weights, tactic weights through
    /// TacticWeightProbe, heights from the snapshot grid. No order position is computed and no engine spatial query is made;
    /// the only per-frame engine call is WorldToScreen.
    ///
    /// COST: texts at most every DecisionRefreshSeconds; positions re-read (cached fields) and projected every frame. Own
    /// pools sized once. Main thread only; instance fields only, cleared with the build.
    /// </summary>
    public partial class TerrainFeatureOverlay
    {
        private const float DecisionRefreshSeconds = 0.5f;
        private const int DecisionLabelCapacity = 40;
        private const int DecisionDotCapacity = 600;

        private const float FormationLift = 3f; // m above the ground under the formation
        private const float TeamLift = 3f; // m above the ground under the team's centre

        /// <summary>Team labels grow upward: px per text line, and the gap over the terrain layer's army label.</summary>
        private const float TeamLabelLineHeight = 18f;
        private const float TeamLabelTerrainGap = 26f;

        private const int TacticRows = 4;
        private const int OrderLineDots = 10;
        private const float OrderLineDotSize = 6f;
        private const float OrderEndDotSize = 10f;

        private const float MinOrderLineLength = 3f; // m; shorter = standing on its order position

        private const float BehaviorChangeHighlightSeconds = 2f;

        private const uint AttackerTeamDecisionColor = 0xFFFF7070u;
        private const uint DefenderTeamDecisionColor = 0xFF7AA0FFu;
        private const uint AttackerFormationDecisionColor = 0xFFFFB0A0u;
        private const uint DefenderFormationDecisionColor = 0xFFB0D0FFu;
        private const uint PlayerFormationDecisionColor = 0xFFC8C8C8u;
        private const uint ChangedDecisionColor = 0xFFFFF080u;

        private sealed class DecisionEntry
        {
            /// <summary>Null for a team entry.</summary>
            public Formation Formation;
            public Team Team;
            public bool IsTeam;
            public string Text;
            public uint Color;
            /// <summary>Text lines; a team label is raised by (Lines - 1) x TeamLabelLineHeight.</summary>
            public int Lines;
            /// <summary>Draw the dotted line to the formation's cached order position (not for charges or the player).</summary>
            public bool OrderLine;
            /// <summary>Team entries: the men-weighted centre of its formations and their mean height (fallback without a grid).</summary>
            public Vec2 Centre;
            public float CentreZ;
        }

        private readonly List<DecisionEntry> _decisionEntries = new List<DecisionEntry>();
        private readonly string[] _decisionSlotTexts = new string[DecisionLabelCapacity];
        private readonly uint[] _decisionSlotColors = new uint[DecisionLabelCapacity];

        /// <summary>Per tactic type, its instance fields of type Formation (declared on it or a base type).</summary>
        private readonly Dictionary<Type, FieldInfo[]> _roleFields = new Dictionary<Type, FieldInfo[]>();
        private readonly Dictionary<Formation, string> _roles = new Dictionary<Formation, string>();
        private readonly Dictionary<Formation, string> _claimTexts = new Dictionary<Formation, string>();
        private readonly List<KeyValuePair<Formation, TerrainSpots.Spot>> _decisionClaims = new List<KeyValuePair<Formation, TerrainSpots.Spot>>();
        private readonly List<TerrainCavalry.Hold> _decisionHolds = new List<TerrainCavalry.Hold>();
        private readonly List<KeyValuePair<BehaviorComponent, float>> _scored = new List<KeyValuePair<BehaviorComponent, float>>();

        /// <summary>Each formation's active behavior when last read and since when (NegativeInfinity = already active when first seen).</summary>
        private readonly Dictionary<Formation, BehaviorComponent> _lastActive = new Dictionary<Formation, BehaviorComponent>();
        private readonly Dictionary<Formation, float> _activeSince = new Dictionary<Formation, float>();

        /// <summary>Each team's tactic when last read, since when, and whether already current when first seen.</summary>
        private readonly Dictionary<Team, TacticComponent> _lastTeamTactic = new Dictionary<Team, TacticComponent>();
        private readonly Dictionary<Team, float> _teamTacticSince = new Dictionary<Team, float>();
        private readonly HashSet<Team> _teamTacticFirstSeen = new HashSet<Team>();
        private readonly Dictionary<Team, string> _teamCultures = new Dictionary<Team, string>();

        private float _decisionNextRefresh;
        private string _decisionLegend;
        private int _decisionFormations;
        private int _decisionPlayerFormations;
        private int _decisionTeams;
        private int _decisionDropped;
        private int _decisionDotsShown;
        private double _decisionTextMs;
        private long _decisionProjectTicks;
        private int _decisionProjectFrames;

        private readonly Comparison<KeyValuePair<BehaviorComponent, float>> _byScoreDescending =
            (a, b) => SafeScore(b.Value).CompareTo(SafeScore(a.Value));

        private static float SafeScore(float score)
        {
            return float.IsNaN(score) ? float.MinValue : score;
        }

        private void ResetDecisions()
        {
            _decisionEntries.Clear();
            Array.Clear(_decisionSlotTexts, 0, _decisionSlotTexts.Length);
            Array.Clear(_decisionSlotColors, 0, _decisionSlotColors.Length);
            _roleFields.Clear();
            _roles.Clear();
            _claimTexts.Clear();
            _decisionClaims.Clear();
            _decisionHolds.Clear();
            _scored.Clear();
            _lastActive.Clear();
            _activeSince.Clear();
            _lastTeamTactic.Clear();
            _teamTacticSince.Clear();
            _teamTacticFirstSeen.Clear();
            _teamCultures.Clear();
            _decisionNextRefresh = float.NegativeInfinity;
            _decisionLegend = null;
            _decisionFormations = 0;
            _decisionPlayerFormations = 0;
            _decisionTeams = 0;
            _decisionDropped = 0;
            _decisionDotsShown = 0;
            _decisionTextMs = 0.0;
            _decisionProjectTicks = 0;
            _decisionProjectFrames = 0;
        }

        private void RefreshDecisions(Camera camera, BattleTerrain terrain)
        {
            _dataSource.EnsureDecisionPools(DecisionLabelCapacity, DecisionDotCapacity);
            float now = Mission.CurrentTime;
            if (now >= _decisionNextRefresh)
            {
                long textStart = DateTime.UtcNow.Ticks;
                RebuildDecisionTexts(now);
                _decisionTextMs = (double)(DateTime.UtcNow.Ticks - textStart) / TimeSpan.TicksPerMillisecond;
                _decisionNextRefresh = now + DecisionRefreshSeconds;
                double projectMs = (_decisionProjectFrames > 0) ? (double)_decisionProjectTicks / TimeSpan.TicksPerMillisecond / _decisionProjectFrames : 0.0;
                _decisionProjectTicks = 0;
                _decisionProjectFrames = 0;
                _decisionLegend = "Decisions: " + _decisionTeams + " teams, " + _decisionFormations + " formations ("
                    + _decisionPlayerFormations + " player)" + (_decisionDropped > 0 ? ", " + _decisionDropped + " not shown (cap " + DecisionLabelCapacity + ")" : "")
                    + ", " + _decisionDotsShown + " order dots | score = GetAIWeight x WeightFactor, xN = active keep bonus, * = changed < "
                    + Fmt0(BehaviorChangeHighlightSeconds) + " s | texts every " + DecisionRefreshSeconds.ToString("0.0", CultureInfo.InvariantCulture)
                    + " s " + _decisionTextMs.ToString("0.00", CultureInfo.InvariantCulture) + " ms, projection "
                    + projectMs.ToString("0.00", CultureInfo.InvariantCulture) + " ms/frame";
                UpdateLegend();
            }
            long startTicks = DateTime.UtcNow.Ticks;
            ProjectDecisions(camera, terrain);
            _decisionProjectTicks += DateTime.UtcNow.Ticks - startTicks;
            _decisionProjectFrames++;
        }

        /// <summary>Every frame: each entry's label at its formation's cached average position (a team's at its centre), and dotted order lines to the cached order position.</summary>
        private void ProjectDecisions(Camera camera, BattleTerrain terrain)
        {
            float inverseScale = _gauntletLayer.UIContext.InverseScale;
            float width = TaleWorlds.Engine.Screen.RealScreenResolutionWidth;
            float height = TaleWorlds.Engine.Screen.RealScreenResolutionHeight;
            bool terrainShown = ShowsTerrain;
            MBBindingList<TerrainFeatureLabelVM> labels = _dataSource.DecisionLabels;
            MBBindingList<NavigableDebugDotVM> dots = _dataSource.DecisionDots;
            int dotCount = 0;

            for (int i = 0; i < labels.Count; i++)
            {
                TerrainFeatureLabelVM label = labels[i];
                if (i >= _decisionEntries.Count)
                {
                    label.IsHidden = true;
                    continue;
                }
                DecisionEntry entry = _decisionEntries[i];
                Vec2 at;
                float fallbackZ;
                Formation formation = entry.Formation;
                if (entry.IsTeam)
                {
                    at = entry.Centre;
                    fallbackZ = entry.CentreZ;
                }
                else if (formation != null && formation.CountOfUnits > 0)
                {
                    at = formation.CachedAveragePosition;
                    fallbackZ = formation.CachedMedianPosition.GetVec3WithoutValidity().z;
                }
                else
                {
                    label.IsHidden = true;
                    continue;
                }
                Vec3 world;
                float sx, sy;
                if (!at.IsValid || !DecisionPoint(terrain, at, fallbackZ, entry.IsTeam ? TeamLift : FormationLift, out world)
                    || !ToScreen(camera, world, width, height, out sx, out sy))
                {
                    label.IsHidden = true;
                }
                else
                {
                    if (_decisionSlotTexts[i] != entry.Text || _decisionSlotColors[i] != entry.Color)
                    {
                        _decisionSlotTexts[i] = entry.Text;
                        _decisionSlotColors[i] = entry.Color;
                        label.SetContent(entry.Text, entry.Color);
                    }
                    float y = sy * inverseScale - LabelHalfHeight;
                    if (entry.IsTeam)
                    {
                        y -= (entry.Lines - 1) * TeamLabelLineHeight + (terrainShown ? TeamLabelTerrainGap : 0f);
                    }
                    label.Place(sx * inverseScale - MarkerSize * 0.5f, y);
                }

                if (entry.OrderLine && formation != null && formation.OrderPositionIsValid && at.IsValid)
                {
                    Vec2 to = formation.OrderPosition;
                    if (to.IsValid && (to - at).Length >= MinOrderLineLength)
                    {
                        for (int d = 0; d <= OrderLineDots && dotCount < dots.Count; d++)
                        {
                            bool end = d == OrderLineDots;
                            Vec2 p = end ? to : at + (to - at) * ((d + 0.5f) / OrderLineDots);
                            Vec3 dotWorld;
                            float dx, dy;
                            if (DecisionPoint(terrain, p, fallbackZ, LabelLift, out dotWorld) && ToScreen(camera, dotWorld, width, height, out dx, out dy))
                            {
                                dots[dotCount].Set(dx * inverseScale, dy * inverseScale, end ? OrderEndDotSize : OrderLineDotSize, DotAlpha, entry.Color);
                                dotCount++;
                            }
                        }
                    }
                }
            }
            _decisionDotsShown = dotCount;
            for (int i = dotCount; i < dots.Count; i++)
            {
                dots[i].IsHidden = true;
            }
        }

        /// <summary>Grid terrain height under p + lift, else fallbackZ + lift; false when neither is usable.</summary>
        private static bool DecisionPoint(BattleTerrain terrain, Vec2 position, float fallbackZ, float lift, out Vec3 world)
        {
            float z = float.NaN;
            if (terrain != null)
            {
                z = terrain.SampleHeight(position);
                if (float.IsNaN(z) || float.IsInfinity(z))
                {
                    z = terrain.HeightAt(position);
                }
            }
            if (float.IsNaN(z) || float.IsInfinity(z))
            {
                z = fallbackZ;
            }
            if (float.IsNaN(z) || float.IsInfinity(z))
            {
                world = Vec3.Invalid;
                return false;
            }
            world = new Vec3(position.x, position.y, z + lift, -1f);
            return true;
        }

        private void RebuildDecisionTexts(float now)
        {
            _decisionEntries.Clear();
            _decisionFormations = 0;
            _decisionPlayerFormations = 0;
            _decisionTeams = 0;
            _decisionDropped = 0;
            if (Mission == null)
            {
                return;
            }
            ReadClaims();
            _roles.Clear();

            // teams first (few labels, carry the ranking), then their formations
            foreach (Team team in Mission.Teams)
            {
                if (team == null || (team.Side != BattleSideEnum.Attacker && team.Side != BattleSideEnum.Defender))
                {
                    continue;
                }
                ReadRoles(team);
                if (team.HasTeamAi)
                {
                    AddTeamEntry(team, now);
                }
            }
            foreach (Team team in Mission.Teams)
            {
                if (team == null || (team.Side != BattleSideEnum.Attacker && team.Side != BattleSideEnum.Defender))
                {
                    continue;
                }
                foreach (Formation formation in team.FormationsIncludingEmpty)
                {
                    if (formation == null || formation.CountOfUnits <= 0)
                    {
                        continue;
                    }
                    if (_decisionEntries.Count >= DecisionLabelCapacity)
                    {
                        _decisionDropped++;
                        continue;
                    }
                    AddFormationEntry(team, formation, now);
                }
            }
        }

        /// <summary>Formation -> "spot A1" / "lane AL1" from the thread-safe snapshots.</summary>
        private void ReadClaims()
        {
            _claimTexts.Clear();
            TerrainSpots.CopyClaims(_decisionClaims);
            foreach (KeyValuePair<Formation, TerrainSpots.Spot> claim in _decisionClaims)
            {
                if (claim.Key != null && claim.Value != null)
                {
                    _claimTexts[claim.Key] = "spot " + claim.Value.Id;
                }
            }
            TerrainCavalry.CopyHolds(_decisionHolds);
            foreach (TerrainCavalry.Hold hold in _decisionHolds)
            {
                if (hold == null || hold.Formation == null)
                {
                    continue;
                }
                string text = "lane " + hold.Id;
                string existing;
                _claimTexts[hold.Formation] = _claimTexts.TryGetValue(hold.Formation, out existing) ? existing + ", " + text : text;
            }
        }

        /// <summary>The team's current tactic's Formation fields that hold one of its own formations -> role names.</summary>
        private void ReadRoles(Team team)
        {
            TacticComponent tactic = TacticWeightProbe.CurrentTactic(team);
            if (tactic == null)
            {
                return;
            }
            Type type = tactic.GetType();
            FieldInfo[] fields;
            if (!_roleFields.TryGetValue(type, out fields))
            {
                List<FieldInfo> found = new List<FieldInfo>();
                for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
                {
                    foreach (FieldInfo field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (field.FieldType == typeof(Formation))
                        {
                            found.Add(field);
                        }
                    }
                }
                fields = found.ToArray();
                _roleFields[type] = fields;
            }
            foreach (FieldInfo field in fields)
            {
                Formation formation;
                try
                {
                    formation = field.GetValue(tactic) as Formation;
                }
                catch
                {
                    formation = null;
                }
                if (formation == null || formation.Team != team)
                {
                    continue;
                }
                string role = RoleName(field.Name);
                string existing;
                if (!_roles.TryGetValue(formation, out existing))
                {
                    _roles[formation] = role;
                }
                else if (!(("/" + existing + "/").Contains("/" + role + "/")))
                {
                    _roles[formation] = existing + "/" + role;
                }
            }
        }

        /// <summary>A tactic field's name as a short role: "_mainInfantry" "main", "_leftCavalry" "left cav", "_rangedCavalry" "HA".</summary>
        private static string RoleName(string fieldName)
        {
            string name = fieldName.TrimStart('_');
            List<string> words = new List<string>();
            StringBuilder word = new StringBuilder();
            foreach (char c in name)
            {
                if ((char.IsUpper(c) || c == '_') && word.Length > 0)
                {
                    words.Add(word.ToString());
                    word.Length = 0;
                }
                if (c != '_')
                {
                    word.Append(char.ToLowerInvariant(c));
                }
            }
            if (word.Length > 0)
            {
                words.Add(word.ToString());
            }
            if (words.Count > 1 && words[words.Count - 1] == "formation")
            {
                words.RemoveAt(words.Count - 1);
            }
            string joined = string.Join(" ", words);
            switch (joined)
            {
                case "main infantry":
                    return "main";
                case "ranged cavalry":
                    return "HA";
            }
            for (int i = 0; i < words.Count; i++)
            {
                if (words[i] == "infantry")
                {
                    words[i] = "inf";
                }
                else if (words[i] == "cavalry")
                {
                    words[i] = "cav";
                }
            }
            return (words.Count > 0) ? string.Join(" ", words) : fieldName;
        }

        private void AddTeamEntry(Team team, float now)
        {
            if (_decisionEntries.Count >= DecisionLabelCapacity)
            {
                _decisionDropped++;
                return;
            }
            // men-weighted centre of the team's formations and their mean height
            Vec2 sum = Vec2.Zero;
            float zSum = 0f;
            int zCount = 0;
            int men = 0;
            foreach (Formation formation in team.FormationsIncludingEmpty)
            {
                if (formation == null || formation.CountOfUnits <= 0)
                {
                    continue;
                }
                Vec2 at = formation.CachedAveragePosition;
                if (!at.IsValid)
                {
                    continue;
                }
                int count = formation.CountOfUnits;
                sum += at * count;
                men += count;
                float z = formation.CachedMedianPosition.GetVec3WithoutValidity().z;
                if (!float.IsNaN(z) && !float.IsInfinity(z))
                {
                    zSum += z;
                    zCount++;
                }
            }
            if (men <= 0)
            {
                return;
            }

            TacticComponent current = TacticWeightProbe.CurrentTactic(team);
            TacticComponent last;
            if (!_lastTeamTactic.TryGetValue(team, out last))
            {
                _lastTeamTactic[team] = current;
                _teamTacticSince[team] = now;
                _teamTacticFirstSeen.Add(team);
            }
            else if (last != current)
            {
                _lastTeamTactic[team] = current;
                _teamTacticSince[team] = now;
                _teamTacticFirstSeen.Remove(team);
            }
            string culture;
            if (!_teamCultures.TryGetValue(team, out culture) || culture == null)
            {
                culture = TacticWeightProbe.ReadCulture(team);
                _teamCultures[team] = culture;
            }

            bool attacker = team.Side == BattleSideEnum.Attacker;
            StringBuilder sb = new StringBuilder();
            sb.Append(attacker ? "ATTACKER" : "DEFENDER");
            if (!string.IsNullOrEmpty(culture))
            {
                sb.Append(' ').Append(culture);
            }
            if (team.IsPlayerTeam)
            {
                sb.Append(" [player team]");
            }
            sb.Append("  tactic: ").Append(TacticWeightProbe.ShortName(current));
            if (current != null)
            {
                sb.Append(" (for ").Append(_teamTacticFirstSeen.Contains(team) ? ">=" : "")
                  .Append((int)(now - _teamTacticSince[team])).Append(" s)");
            }
            int lines = 1;
            List<TacticWeightProbe.RankedTactic> ranked = TacticWeightProbe.Rank(team);
            if (ranked == null)
            {
                sb.Append("\n  no options");
                lines++;
            }
            else
            {
                for (int i = 0; i < ranked.Count && i < TacticRows; i++)
                {
                    TacticWeightProbe.RankedTactic option = ranked[i];
                    sb.Append('\n').Append(option.Tactic == current ? "> " : "  ").Append(i + 1).Append(' ')
                      .Append(TacticWeightProbe.ShortName(option.Tactic)).Append(' ')
                      .Append(float.IsNaN(option.Final) ? "err" : TacticWeightProbe.FormatWeight(option.Final))
                      .Append(" (").Append(float.IsNaN(option.Fit) ? "-" : TacticWeightProbe.FormatWeight(option.Fit))
                      .Append(" x ").Append(option.Registered ? TacticWeightProbe.FormatWeight(option.Multiplier) : "-")
                      .Append(" x ").Append(TacticWeightProbe.FormatWeight(option.Terrain)).Append(')');
                    lines++;
                }
            }

            DecisionEntry entry = new DecisionEntry();
            entry.Team = team;
            entry.IsTeam = true;
            entry.Text = sb.ToString();
            entry.Color = attacker ? AttackerTeamDecisionColor : DefenderTeamDecisionColor;
            entry.Lines = lines;
            entry.Centre = sum * (1f / men);
            entry.CentreZ = (zCount > 0) ? zSum / zCount : float.NaN;
            _decisionEntries.Add(entry);
            _decisionTeams++;
        }

        private void AddFormationEntry(Team team, Formation formation, float now)
        {
            DecisionEntry entry = new DecisionEntry();
            entry.Formation = formation;
            entry.Team = team;
            entry.Lines = 2;
            bool attacker = team.Side == BattleSideEnum.Attacker;
            try
            {
                FillFormationEntry(entry, formation, attacker, now);
            }
            catch (Exception e)
            {
                // one unreadable formation says so on its own label
                entry.Text = FormationName(formation) + "  read failed: " + e.GetType().Name;
                entry.Color = attacker ? AttackerFormationDecisionColor : DefenderFormationDecisionColor;
                entry.OrderLine = false;
            }
            _decisionEntries.Add(entry);
            _decisionFormations++;
        }

        private void FillFormationEntry(DecisionEntry entry, Formation formation, bool attacker, float now)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(FormationName(formation));
            MovementOrder.MovementOrderEnum orderEnum = formation.GetReadonlyMovementOrderReference().OrderEnum;
            bool charge = orderEnum == MovementOrder.MovementOrderEnum.Charge || orderEnum == MovementOrder.MovementOrderEnum.ChargeToTarget;

            if (!formation.IsAIControlled)
            {
                _decisionPlayerFormations++;
                sb.Append(" player\n").Append(OrderText(formation, orderEnum, charge));
                entry.Text = sb.ToString();
                entry.Color = PlayerFormationDecisionColor;
                entry.OrderLine = false;
                return;
            }

            string role;
            sb.Append(' ').Append(_roles.TryGetValue(formation, out role) ? role : "-");

            // behavior contest as FindBestBehavior runs it (candidates: WeightFactor > 1e-7)
            BehaviorComponent active = (formation.AI != null) ? formation.AI.ActiveBehavior : null;
            bool changed = false;
            if (formation.AI != null)
            {
                BehaviorComponent lastActive;
                if (!_lastActive.TryGetValue(formation, out lastActive))
                {
                    _lastActive[formation] = active;
                    _activeSince[formation] = float.NegativeInfinity;
                }
                else if (lastActive != active)
                {
                    _lastActive[formation] = active;
                    _activeSince[formation] = now;
                }
                float since;
                changed = _activeSince.TryGetValue(formation, out since) && now - since < BehaviorChangeHighlightSeconds;

                _scored.Clear();
                int count = formation.AI.BehaviorCount;
                for (int i = 0; i < count; i++)
                {
                    BehaviorComponent behavior = formation.AI.GetBehaviorAtIndex(i);
                    if (behavior == null || !(behavior.WeightFactor > 1E-07f))
                    {
                        continue;
                    }
                    float weight;
                    try
                    {
                        weight = behavior.GetAIWeight();
                    }
                    catch
                    {
                        weight = float.NaN;
                    }
                    _scored.Add(new KeyValuePair<BehaviorComponent, float>(behavior, weight * behavior.WeightFactor));
                }
                _scored.Sort(_byScoreDescending);
            }
            else
            {
                _scored.Clear();
            }

            sb.Append("  ").Append(changed ? "* " : "").Append("> ");
            if (active == null)
            {
                sb.Append('-');
            }
            else
            {
                sb.Append(BehaviorName(active)).Append(' ');
                float activeScore = float.NaN;
                bool candidate = false;
                foreach (KeyValuePair<BehaviorComponent, float> scored in _scored)
                {
                    if (scored.Key == active)
                    {
                        activeScore = scored.Value;
                        candidate = true;
                        break;
                    }
                }
                sb.Append(candidate ? ScoreText(activeScore) : "w0");
                // native's keep bonus for the active behavior: x Lerp(1.2, 2) as PreserveExpireTime runs out over 5 s
                float keep = MBMath.Lerp(1.2f, 2f, MBMath.ClampFloat((active.PreserveExpireTime - now) / 5f, 0f, 1f), float.MinValue);
                sb.Append(" x").Append(keep.ToString("0.0#", CultureInfo.InvariantCulture));
            }
            int others = 0;
            foreach (KeyValuePair<BehaviorComponent, float> scored in _scored)
            {
                if (others >= 2)
                {
                    break;
                }
                if (scored.Key == active)
                {
                    continue;
                }
                sb.Append(" | ").Append(BehaviorName(scored.Key)).Append(' ').Append(ScoreText(scored.Value));
                others++;
            }

            sb.Append('\n').Append(OrderText(formation, orderEnum, charge));
            string claim;
            if (_claimTexts.TryGetValue(formation, out claim))
            {
                sb.Append("  ").Append(claim);
            }
            string shape = TerrainShape.Describe(formation);
            if (shape != null)
            {
                sb.Append("  ").Append(shape);
            }
            string arrangement = RBMAI.Utilities.DescribeRangedArrangement(formation);
            if (arrangement != null)
            {
                sb.Append("  ").Append(arrangement);
            }
            entry.Text = sb.ToString();
            entry.Color = changed ? ChangedDecisionColor : (attacker ? AttackerFormationDecisionColor : DefenderFormationDecisionColor);
            entry.OrderLine = !charge;
        }

        /// <summary>"Move 45m" (distance to the cached order position), "Charge -> DEF Cav#2", "Charge (nearest DEF Inf#0)".</summary>
        private static string OrderText(Formation formation, MovementOrder.MovementOrderEnum orderEnum, bool charge)
        {
            string text = orderEnum.ToString();
            if (charge)
            {
                Formation target = formation.GetReadonlyMovementOrderReference().TargetFormation;
                if (target != null && target.CountOfUnits > 0)
                {
                    return text + " -> " + FormationName(target);
                }
                FormationQuerySystem nearest = formation.CachedClosestEnemyFormation;
                if (nearest != null && nearest.Formation != null && nearest.Formation.CountOfUnits > 0)
                {
                    return text + " (nearest " + FormationName(nearest.Formation) + ")";
                }
                return text;
            }
            // ChargeGuard: a charge walked as an advance until its target is within 50 m
            Formation advanceTarget = ChargeGuard.AdvanceTarget(formation);
            if (advanceTarget != null)
            {
                text += "(charge advance -> " + FormationName(advanceTarget) + ")";
            }
            if (formation.OrderPositionIsValid)
            {
                Vec2 at = formation.CachedAveragePosition;
                Vec2 to = formation.OrderPosition;
                if (at.IsValid && to.IsValid)
                {
                    text += " " + Fmt0(at.Distance(to)) + "m";
                }
            }
            return text;
        }

        /// <summary>"ATK Inf#0": side, main class (QuerySystem.MainClass, as AiBehaviorLogic reads it), formation index.</summary>
        private static string FormationName(Formation formation)
        {
            string side = (formation.Team != null && formation.Team.Side == BattleSideEnum.Attacker) ? "ATK" : "DEF";
            string cls;
            try
            {
                cls = (formation.QuerySystem != null) ? ClassName(formation.QuerySystem.MainClass) : "?";
            }
            catch
            {
                cls = "?";
            }
            return side + " " + cls + "#" + (int)formation.FormationIndex;
        }

        private static string ClassName(FormationClass formationClass)
        {
            switch (formationClass)
            {
                case FormationClass.Infantry:
                    return "Inf";
                case FormationClass.Ranged:
                    return "Rng";
                case FormationClass.Cavalry:
                    return "Cav";
                case FormationClass.HorseArcher:
                    return "HA";
                case FormationClass.Skirmisher:
                    return "Skirm";
                case FormationClass.HeavyInfantry:
                    return "HInf";
                case FormationClass.LightCavalry:
                    return "LCav";
                case FormationClass.HeavyCavalry:
                    return "HCav";
                case FormationClass.General:
                    return "Gen";
                case FormationClass.Bodyguard:
                    return "Guard";
                default:
                    return formationClass.ToString();
            }
        }

        /// <summary>The behavior's type name without its "RBMBehavior" / "Behavior" prefix.</summary>
        private static string BehaviorName(BehaviorComponent behavior)
        {
            // behaviors with a state name show it, e.g. "CavalryCharge(reform through)", "HoldChoke(holding)"
            RBMBehaviorCavalryCharge charge = behavior as RBMBehaviorCavalryCharge;
            if (charge != null && charge.ReformKindName != null)
            {
                return "CavalryCharge(reform " + charge.ReformKindName + ")";
            }
            RBMBehaviorHoldChoke hold = behavior as RBMBehaviorHoldChoke;
            if (hold != null)
            {
                return "HoldChoke(" + hold.StateName + ")";
            }
            RBMBehaviorHoldPosition position = behavior as RBMBehaviorHoldPosition;
            if (position != null)
            {
                return "HoldPosition(" + position.StateName + ")";
            }
            RBMBehaviorReservePlug plug = behavior as RBMBehaviorReservePlug;
            if (plug != null)
            {
                return "ReservePlug(" + plug.StateName + ")";
            }
            RBMBehaviorHedgehog hedgehog = behavior as RBMBehaviorHedgehog;
            if (hedgehog != null)
            {
                return "Hedgehog(" + hedgehog.StateName + ")";
            }
            RBMBehaviorReact react = behavior as RBMBehaviorReact;
            if (react != null)
            {
                return "React(" + react.StateName + ")";
            }
            RBMBehaviorFollowCavalry follow = behavior as RBMBehaviorFollowCavalry;
            if (follow != null)
            {
                return "FollowCavalry(" + follow.StateName + ")";
            }
            RBMBehaviorArcherStand stand = behavior as RBMBehaviorArcherStand;
            if (stand != null)
            {
                return "ArcherStand(" + stand.StateName + ")";
            }
            // support fire once the frontlines connect (SupportFire)
            RBMBehaviorArcherSkirmish skirmish = behavior as RBMBehaviorArcherSkirmish;
            if (skirmish != null && skirmish.SupportLabel != null)
            {
                return "ArcherSkirmish(" + skirmish.SupportLabel + ")";
            }
            RBMBehaviorArcherFlank flank = behavior as RBMBehaviorArcherFlank;
            if (flank != null && flank.SupportLabel != null)
            {
                return "ArcherFlank(" + flank.SupportLabel + ")";
            }
            string name = behavior.GetType().Name;
            if (name.StartsWith("RBMBehavior"))
            {
                return name.Substring("RBMBehavior".Length);
            }
            if (name.StartsWith("Behavior"))
            {
                return name.Substring("Behavior".Length);
            }
            return name;
        }

        private static string ScoreText(float score)
        {
            return float.IsNaN(score) ? "err" : TacticWeightProbe.FormatWeight(score);
        }
    }
}
