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

        public override void AfterStart()
        {
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
        }
    }
}