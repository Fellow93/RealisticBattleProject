using System.Collections.Generic;
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

        // Melee blocks of this battle, both sides together, indexed by MeleeBlockKind.
        private readonly int[] _blocks = new int[RBMConfig.MeleeBlock.KindCount];

        // Direct melee hits of this battle, both sides together: a weapon (or fist) landing on a man,
        // not a horse charge, kick or bash. The battle hit log's "melee" rows count the same thing.
        private int _meleeHits;

        public override void AfterStart()
        {
            // The kick/bash counters are per battle.
            AgentAi.AiKickBash.ResetCounters();
            System.Array.Clear(_blocks, 0, _blocks.Length);
            _meleeHits = 0;
            _missionScreen = TaleWorlds.ScreenSystem.ScreenManager.TopScreen as MissionScreen;
            if (_missionScreen == null)
            {
                // No mission screen (headless/spectator/teardown) - run without any UI.
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
            if (_dataSource != null)
            {
                _dataSource.OnFinalize();
                _dataSource = null;
            }
            base.OnRemoveBehavior();
        }

        // The aggregation below is a full walk of the damage dictionary plus eight TextObject
        // allocations, so it runs on a 1s timer instead of on every single hit. Nothing per-hit is
        // kept here: Tactics.CustomBattleAgentLogicOnAgentHitPatch already records each blow into
        // agentDamage a frame later, which is what this reads.
        private const float RefreshInterval = 1f;
        private float _timeSinceRefresh = RefreshInterval;

        // A blocked melee blow registers no Blow, so it never reaches OnAgentHit; the melee
        // collision is the only place it is seen. Just a counter bump; the text is built on the timer.
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
            // A horse as the affector is a charge or trample, so a human striker is required.
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
            _dataSource.Kickbash = RBMConfig.RBMConfig.aiKickBashEnabled
                ? new TextObject("{=RBM_AI_025}KICK/BASH:").ToString() + " " + AgentAi.AiKickBash.CountersText()
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