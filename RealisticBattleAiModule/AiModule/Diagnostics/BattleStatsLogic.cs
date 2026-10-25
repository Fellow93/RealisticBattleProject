using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using static RBMAI.Tactics;

namespace RBMAI
{
    public class BattleStatsLogic : MissionLogic
    {
        public BattleStatsVM _dataSource;

        private GauntletLayer _gauntletLayer;
        private MissionScreen _missionScreen;

        public bool IsEnabled
        {
            get
            {
                bool result = true;
                return result;
            }
        }

        private readonly int[] _blocks = new int[RBMConfig.MeleeBlock.KindCount];

        // Direct melee hits (weapon or fist on a man; not charge, kick or bash).
        private int _meleeHits;

        public override void AfterStart()
        {
            RBMStance.KickBash.AiKickBash.ResetCounters();
            System.Array.Clear(_blocks, 0, _blocks.Length);
            _meleeHits = 0;
            _missionScreen = TaleWorlds.ScreenSystem.ScreenManager.TopScreen as MissionScreen;
            if (_missionScreen == null)
            {
                // No mission screen (headless/spectator/teardown): run without UI.
                return;
            }
            _dataSource = new BattleStatsVM();
            _gauntletLayer = new GauntletLayer("GauntletLayer" ,- 1);
            _missionScreen.AddLayer(_gauntletLayer);
            _gauntletLayer.LoadMovie("BattleStats", (ViewModel)_dataSource);
        }

        public override void OnRemoveBehavior()
        {
            if (_missionScreen != null && _gauntletLayer != null)
            {
                _missionScreen.RemoveLayer(_gauntletLayer);
            }
            _gauntletLayer = null;
            _missionScreen = null;
            _tacticTeams.Clear();
            _eligibleTeams.Clear();
            if (_dataSource != null)
            {
                _dataSource.OnFinalize();
                _dataSource = null;
            }
            base.OnRemoveBehavior();
        }

        // The aggregation walks the whole damage dictionary, so it runs on a 1 s timer, not per hit.
        private const float RefreshInterval = 1f;
        private float _timeSinceRefresh = RefreshInterval;

        // A blocked melee blow registers no Blow (never reaches OnAgentHit); the collision is the only place it is seen.
        public override void OnMeleeHit(Agent attacker, Agent victim, bool isCanceled, AttackCollisionData collisionData)
        {
            if (attacker == null || victim == null || !attacker.IsHuman || !victim.IsHuman || !attacker.IsEnemyOf(victim))
            {
                return;
            }
            RBMConfig.MeleeBlockKind kind = RBMConfig.MeleeBlock.Classify(in collisionData);
            if (kind != RBMConfig.MeleeBlockKind.None)
            {
                _blocks[(int)kind]++;
            }
        }

        public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent, in MissionWeapon affectorWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
        {
            // A horse as the affector is a charge/trample: require a human striker.
            if (affectedAgent == null || affectorAgent == null || !affectedAgent.IsHuman || !affectorAgent.IsHuman
                || !affectorAgent.IsEnemyOf(affectedAgent))
            {
                return;
            }
            if (attackCollisionData.IsMissile || attackCollisionData.IsHorseCharge || attackCollisionData.IsFallDamage
                || blow.AttackType == AgentAttackType.Kick || blow.AttackType == AgentAttackType.Bash)
            {
                return;
            }
            _meleeHits++;
        }

        private string BlockLine(string label, RBMConfig.MeleeBlockKind kind)
        {
            return new TextObject(label).ToString() + " " + _blocks[(int)kind];
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (_dataSource == null)
            {
                return;
            }
            _timeSinceRefresh += dt;
            if (_timeSinceRefresh < RefreshInterval)
            {
                return;
            }
            _timeSinceRefresh = 0f;
            RefreshStats();
            RefreshTactics();
        }

        // ---- tactics panel (main thread, 1 s timer) ----

        private sealed class TacticPanelTeam
        {
            public Team Team;
            public BattleStatsTacticTeamVM Vm;
            // null until read
            public string Culture;
            public TacticComponent LastTactic;
            public float LastTacticSince;
        }

        private readonly List<TacticPanelTeam> _tacticTeams = new List<TacticPanelTeam>();
        private readonly List<Team> _eligibleTeams = new List<Team>();

        private void RefreshTactics()
        {
            if (Mission == null)
            {
                return;
            }
            _eligibleTeams.Clear();
            foreach (Team team in Mission.Teams)
            {
                if (team != null && team.TeamAI != null
                    && (team.Side == BattleSideEnum.Attacker || team.Side == BattleSideEnum.Defender))
                {
                    _eligibleTeams.Add(team);
                }
            }
            bool same = _eligibleTeams.Count == _tacticTeams.Count;
            for (int i = 0; same && i < _eligibleTeams.Count; i++)
            {
                same = _eligibleTeams[i] == _tacticTeams[i].Team;
            }
            if (!same)
            {
                _tacticTeams.Clear();
                _dataSource.TacticTeams.Clear();
                foreach (Team team in _eligibleTeams)
                {
                    TacticPanelTeam entry = new TacticPanelTeam();
                    entry.Team = team;
                    entry.Vm = new BattleStatsTacticTeamVM();
                    _tacticTeams.Add(entry);
                    _dataSource.TacticTeams.Add(entry.Vm);
                }
            }
            float now = Mission.CurrentTime;
            foreach (TacticPanelTeam entry in _tacticTeams)
            {
                RefreshTacticTeam(entry, now);
            }
            RefreshTerrainMap();
        }

        private void RefreshTerrainMap()
        {
            BattleTerrain terrain = BattleTerrain.Current;
            if (terrain == null)
            {
                _dataSource.TerrainMapLine = "map: not read yet (land field battles, a few frames after deployment)";
                _dataSource.TerrainFeatureLine = "features: -  (on the map: Ctrl+Shift+D)";
                return;
            }
            BattleTerrain.OpenInfo open = terrain.Open;
            bool mid = !float.IsNaN(open.MidCorridorWidth);
            _dataSource.TerrainMapLine = "map: " + (mid ? "mid corridor " : "corridor ")
                + TerrainFeatureOverlay.Fmt0(mid ? open.MidCorridorWidth : open.CorridorWidth)
                + " m = " + TerrainFeatureOverlay.Fmt1(mid ? open.MidCorridorFront : open.CorridorFront) + "x wider frontage";

            string features = "features: " + Count(terrain.Hills.Count, "hill", "hills")
                + ", " + Count(terrain.Crests.Count, "crest", "crests")
                + ", " + Count(terrain.Chokes.Count, "choke", "chokes")
                + ", " + Count(terrain.OpenFields.Count, "open field", "open fields")
                + ", " + Count(terrain.Woods.Count, "woods", "woods")
                + ", " + Count(terrain.Rough.Count, "rough", "rough")
                + ", " + Count(terrain.Waters.Count, "water", "waters")
                + ", " + Count(terrain.Fords.Count, "ford", "fords");
            if (terrain.FeatureMap.Error != null)
            {
                features += " (errors: " + terrain.FeatureMap.Error + ")";
            }
            _dataSource.TerrainFeatureLine = features + "  (on the map: Ctrl+Shift+D)";
        }

        private static string Count(int count, string singular, string plural)
        {
            return count + " " + (count == 1 ? singular : plural);
        }

        private static string FlankLine(Team team)
        {
            if (!RBMConfig.RBMConfig.terrainTacticsEnabled)
            {
                return "flank plan: off";
            }
            TerrainFlankPlan.SidePlan plan = TerrainFlankPlan.ForSide(team.Side);
            BattleTerrain terrain = BattleTerrain.Current;
            if (plan == null || terrain == null)
            {
                return "flank plan: not read yet";
            }
            BattleTerrain.ArmySet own = (team.Side == BattleSideEnum.Attacker) ? BattleTerrain.ArmySet.Attacker : BattleTerrain.ArmySet.Defender;
            BattleTerrain.ArmySet enemy = (own == BattleTerrain.ArmySet.Attacker) ? BattleTerrain.ArmySet.Defender : BattleTerrain.ArmySet.Attacker;
            string line = "flanks: left " + FlankSideText(plan.OwnLeftCovered, plan.OwnLeftReason, TerrainFeatureOverlay.FindFlank(terrain, own, true))
                + " | right " + FlankSideText(plan.OwnRightCovered, plan.OwnRightReason, TerrainFeatureOverlay.FindFlank(terrain, own, false));
            if (plan.HasAttackSide)
            {
                bool left = plan.AttackSide == FormationAI.BehaviorSide.Left;
                // Our right goes round the enemy's own left (it faces us).
                BattleTerrain.FlankFeature target = TerrainFeatureOverlay.FindFlank(terrain, enemy, !left);
                float seconds = (target != null && target.EnemyRoute != null) ? target.EnemyRoute.Seconds : float.NaN;
                line += " - attack: " + (left ? "left" : "right") + " (route " + TerrainFeatureOverlay.Fmt0(seconds) + " s, " + plan.AttackReason + ")";
            }
            else
            {
                line += " - attack: none (" + plan.AttackReason + ")";
            }
            return line;
        }

        private static string FlankSideText(bool covered, string reason, BattleTerrain.FlankFeature flank)
        {
            string text = (covered ? "covered (" : "open (") + reason;
            if (flank != null && !float.IsNaN(flank.EndDistance))
            {
                text += ", end " + TerrainFeatureOverlay.Fmt0(flank.EndDistance) + " m";
            }
            return text + ")";
        }

        private void RefreshTacticTeam(TacticPanelTeam entry, float now)
        {
            Team team = entry.Team;
            TacticComponent current = TacticWeightProbe.CurrentTactic(team);
            if (current != entry.LastTactic)
            {
                entry.LastTactic = current;
                entry.LastTacticSince = now;
            }
            if (entry.Culture == null)
            {
                entry.Culture = ReadCulture(team);
            }

            string title = team.Side.ToString();
            if (!string.IsNullOrEmpty(entry.Culture))
            {
                title += " (" + entry.Culture + ")";
            }
            if (team.IsPlayerTeam)
            {
                title += " [player team]";
            }
            title += " - " + ShortTacticName(current);
            if (current != null)
            {
                title += " for " + (int)(now - entry.LastTacticSince) + "s";
            }
            entry.Vm.Title = title;
            entry.Vm.TerrainLine = TerrainLine(team, current);
            entry.Vm.FlankLine = FlankLine(team);

            List<TacticWeightProbe.RankedTactic> ranked = TacticWeightProbe.Rank(team);
            if (ranked == null)
            {
                entry.Vm.SetRowCount(0);
                entry.Vm.Title = title + " - no options";
                return;
            }
            entry.Vm.SetRowCount(ranked.Count);
            for (int i = 0; i < ranked.Count; i++)
            {
                TacticWeightProbe.RankedTactic option = ranked[i];
                BattleStatsTacticRowVM row = entry.Vm.Rows[i];
                bool isCurrent = option.Tactic == current;
                row.IsCurrent = isCurrent;
                row.Rank = (isCurrent ? "> " : "") + (i + 1);
                row.Name = ShortTacticName(option.Tactic);
                row.Final = float.IsNaN(option.Final) ? "err" : FormatWeight(option.Final);
                row.Multiplier = option.Registered ? FormatWeight(option.Multiplier) : "-";
                row.Terrain = FormatWeight(option.Terrain);
                // Army fit: composition grade before culture/commander/jitter and terrain.
                row.Fit = float.IsNaN(option.Fit) ? "-" : FormatWeight(option.Fit);
            }
        }

        private static string ReadCulture(Team team)
        {
            return TacticWeightProbe.ReadCulture(team);
        }

        private static string TerrainLine(Team team, TacticComponent current)
        {
            if (!RBMConfig.RBMConfig.terrainTacticsEnabled)
            {
                return "terrain: off";
            }
            TerrainTacticFactors.SideFactors factors = TerrainTacticFactors.ForSide(team.Side);
            if (factors == null)
            {
                return "terrain: not read yet";
            }
            TerrainTacticFactors.Factor factor = (current != null) ? factors.Get(current.GetType()) : null;
            if (factor == null)
            {
                return "terrain: applied";
            }
            return "terrain: applied - " + ShortTacticName(current) + " " + factor.DescribeTerms()
                + " (raw " + FormatWeight(factor.Raw) + ")";
        }

        private static string ShortTacticName(TacticComponent tactic)
        {
            return TacticWeightProbe.ShortName(tactic);
        }

        private static string FormatWeight(float value)
        {
            return TacticWeightProbe.FormatWeight(value);
        }

        private void RefreshStats()
        {
            float atkarc = 0;
            float atkha = 0;
            float atkcav = 0;
            float atkinf = 0;
            float defarc = 0;
            float defha = 0;
            float defcav = 0;
            float definf = 0;
            foreach (KeyValuePair<Agent, AgentDamageDone> entry in agentDamage)
            {
                if (entry.Value.isAttacker)
                {
                    if (entry.Value.initialClass == FormationClass.Ranged)
                    {
                        atkarc += entry.Value.damageDone;
                    }
                    if (entry.Value.initialClass == FormationClass.HorseArcher)
                    {
                        atkha += entry.Value.damageDone;
                    }
                    if (entry.Value.initialClass == FormationClass.Cavalry)
                    {
                        atkcav += entry.Value.damageDone;
                    }
                    if (entry.Value.initialClass == FormationClass.Infantry)
                    {
                        atkinf += entry.Value.damageDone;
                    }
                }
                if (!entry.Value.isAttacker)
                {
                    if (entry.Value.initialClass == FormationClass.Ranged)
                    {
                        defarc += entry.Value.damageDone;
                    }
                    if (entry.Value.initialClass == FormationClass.HorseArcher)
                    {
                        defha += entry.Value.damageDone;
                    }
                    if (entry.Value.initialClass == FormationClass.Cavalry)
                    {
                        defcav += entry.Value.damageDone;
                    }
                    if (entry.Value.initialClass == FormationClass.Infantry)
                    {
                        definf += entry.Value.damageDone;
                    }
                }
            }
            _dataSource.Atkarc = new TextObject("{=RBM_AI_001}ATK ARC:").ToString() + atkarc;
            _dataSource.Atkha = new TextObject("{=RBM_AI_002}ATK HA :").ToString() + atkha;
            _dataSource.Atkcav = new TextObject("{=RBM_AI_003}ATK CAV:").ToString() + atkcav;
            _dataSource.Atkinf = new TextObject("{=RBM_AI_004}ATK INF:").ToString() + atkinf;
            _dataSource.Defarc = new TextObject("{=RBM_AI_005}DEF ARC:").ToString() + defarc;
            _dataSource.Defha = new TextObject("{=RBM_AI_006}DEF HA :").ToString() + defha;
            _dataSource.Defcav = new TextObject("{=RBM_AI_007}DEF CAV:").ToString() + defcav;
            _dataSource.Definf = new TextObject("{=RBM_AI_008}DEF INF:").ToString() + definf;
            _dataSource.Kickbash = RBMConfig.RBMConfig.rbmStanceEnabled && RBMConfig.RBMConfig.aiKickBashEnabled
                ? new TextObject("{=RBM_AI_025}KICK/BASH:").ToString() + " " + RBMStance.KickBash.AiKickBash.CountersText()
                : "";
            _dataSource.Chamberblocks = BlockLine("{=RBM_AI_026}CHAMBER BLOCKS:", RBMConfig.MeleeBlockKind.ChamberBlock);
            _dataSource.Weaponblocks = BlockLine("{=RBM_AI_028}WEAPON BLOCKS:", RBMConfig.MeleeBlockKind.WeaponBlock);
            _dataSource.Weaponparries = BlockLine("{=RBM_AI_029}WEAPON PARRIES:", RBMConfig.MeleeBlockKind.WeaponParry);
            _dataSource.Shieldblocks = BlockLine("{=RBM_AI_030}SHIELD BLOCKS:", RBMConfig.MeleeBlockKind.ShieldBlock);
            _dataSource.Shieldparries = BlockLine("{=RBM_AI_031}SHIELD PARRIES:", RBMConfig.MeleeBlockKind.ShieldParry);
            _dataSource.Shieldwrongside = BlockLine("{=RBM_AI_032}WRONG-SIDE SHIELD:", RBMConfig.MeleeBlockKind.ShieldWrongSide);
            _dataSource.Meleehits = new TextObject("{=RBM_AI_027}MELEE HITS:").ToString() + " " + _meleeHits;
        }
    }
}