using System.Linq;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace RBM.AgentStatusBar
{
    public class UnitStatusMissionView : MissionLogic
    {
        private UnitStatusVM _dataSource;

        private GauntletLayer _gauntletLayer;

        public override void AfterStart()
        {
            MissionScreen missionScreen = ScreenManager.TopScreen as MissionScreen;
            _dataSource = new UnitStatusVM(missionScreen.CombatCamera);
            _gauntletLayer = new GauntletLayer("AgentStatusLayer", 1);
            missionScreen.AddLayer(_gauntletLayer);
            _gauntletLayer.LoadMovie("UnitStatus", (ViewModel)_dataSource);
            foreach (Agent agent in Mission.Current.Agents)
            {
                _dataSource.OnAgentCreated(agent);
            }
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (_dataSource == null || Mission.Current.Teams?.FirstOrDefault() == null)
            {
                return;
            }
            if ((Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl))
                && Input.IsKeyPressed(InputKey.H))
            {
                _dataSource.ToggleVisibility();
            }
            _dataSource.Tick(dt);
        }

        protected override void OnEndMission()
        {
            base.OnEndMission();
            if (_gauntletLayer != null)
            {
                MissionScreen missionScreen = ScreenManager.TopScreen as MissionScreen;
                missionScreen?.RemoveLayer(_gauntletLayer);
                _dataSource?.OnFinalize();
                _dataSource = null;
                _gauntletLayer = null;
            }
        }

        public override void OnAgentCreated(Agent agent)
        {
            base.OnAgentCreated(agent);
            _dataSource?.OnAgentCreated(agent);
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, TaleWorlds.Core.AgentState agentState, KillingBlow blow)
        {
            base.OnAgentRemoved(affectedAgent, affectorAgent, agentState, blow);
            if (_dataSource != null && !affectedAgent.IsMount)
            {
                bool isMainAgent = affectorAgent?.IsMainAgent ?? false;
                _dataSource.OnAgentRemoved(affectedAgent, isMainAgent);
            }
        }

        public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent, in MissionWeapon affectorWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
        {
            base.OnAgentHit(affectedAgent, affectorAgent, affectorWeapon, blow, attackCollisionData);
            if (_dataSource != null && !affectedAgent.IsMount)
            {
                bool isMainAgent = affectorAgent?.IsMainAgent ?? false;
                _dataSource.OnAgentHit(affectedAgent, isMainAgent);
            }
        }
    }
}