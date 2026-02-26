// TroopHealthBar.UnitStatusVM
using RBM.AgentStatusBar;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace RBM.AgentStatusBar
{
    public class UnitStatusVM : ViewModel
    {
        private Dictionary<Agent, AgentStatusVM> _agentStatusMap;

        private MBBindingList<AgentStatusVM> _agentStatusList;

        private Camera _camera;

        private Agent _mainAgent;

        private bool _escapeMenuOpened = false;

        private bool _keyToggled = true;

        private Dictionary<Agent, AgentStatusVM> _agentDictionary;

        private List<Agent> _newAgents = new List<Agent>();

        [DataSourceProperty]
        public MBBindingList<AgentStatusVM> AgentStatus => _agentStatusList;

        public UnitStatusVM(Camera camera)
        {
            _camera = camera;
            _agentStatusMap = new Dictionary<Agent, AgentStatusVM>();
            _agentStatusList = new MBBindingList<AgentStatusVM>();
        }

        public override void OnFinalize()
        {
            _agentStatusMap.ToList().ForEach(delegate (KeyValuePair<Agent, AgentStatusVM> entry)
            {
                entry.Value.OnFinalize();
            });
            _agentStatusMap = null;
            _camera = null;
            _mainAgent = null;
            base.OnFinalize();
        }

        public void ToggleVisibility()
        {
            _keyToggled = !_keyToggled;
        }

        public void Tick(float dt)
        {
            RefreshAgentStatus(dt);
        }

        private void RefreshAgentStatus(float dt)
        {
            if (_newAgents.Count > 0)
            {
                foreach (Agent agent in _newAgents.Where((Agent a) => !a.IsMainAgent && !a.IsMount && a.Team != null))
                {
                    AddAgentToMap(agent);
                }
                _newAgents.Clear();
            }
            _agentDictionary = new Dictionary<Agent, AgentStatusVM>(_agentStatusMap);
            foreach (KeyValuePair<Agent, AgentStatusVM> entry2 in _agentDictionary)
            {
                float num0;
                float num1;
                float num2;
                Vec3 pos;
                Vec3 playerPos;
                if (entry2.Value.IsEnabled)
                {
                    num0 = 0f;
                    num1 = 0f;
                    num2 = 0f;
                    pos = entry2.Value._agent.Position;
                    float zOffset = ((!entry2.Key.HasMount) ? 2.2f : 3.2f);
                    pos.z += zOffset;
                    MBWindowManager.WorldToScreen(_camera, pos, ref num0, ref num1, ref num2);
                    Agent mainAgent = _mainAgent;
                    if (mainAgent != null)
                    {
                        _ = mainAgent.Position;
                        if (true)
                        {
                            playerPos = _mainAgent.Position;
                            goto IL_015e;
                        }
                    }
                    playerPos = _camera.Position;
                    goto IL_015e;
                }
                entry2.Value.IsHidden = true;
                continue;
            IL_015e:
                entry2.Value.Distance = pos.Distance(playerPos);
                if (!_keyToggled && !_escapeMenuOpened && num2 >= 0f && (entry2.Value._isHit || entry2.Value.Distance <= (float)AgentStatusBarConfig.DistanceCutOff || entry2.Value.IsAgentFocused))
                {
                    entry2.Value.IsHidden = false;
                    entry2.Value.ScreenZ = Convert.ToInt32(num2);
                    entry2.Value.ScreenDistance = pos.Distance(_camera.Position);
                    entry2.Value.ScreenXPosition = num0;
                    entry2.Value.ScreenYPosition = num1;
                    entry2.Value.tick(dt, _keyToggled);
                }
                else
                {
                    entry2.Value.IsHidden = true;
                }
            }
            foreach (KeyValuePair<Agent, AgentStatusVM> entry in _agentDictionary.Where((KeyValuePair<Agent, AgentStatusVM> e) => e.Value._isRemoved && !e.Value._isHit))
            {
                _agentStatusMap[entry.Key].IsEnabled = false;
                _agentStatusMap[entry.Key] = null;
                _agentStatusMap.Remove(entry.Key);
                _agentStatusList.Remove(entry.Value);
            }
        }

        public void OnAgentHit(Agent agent, bool isMainAgent)
        {
            if (_agentStatusMap.ContainsKey(agent))
            {
                _agentStatusMap[agent].OnAgentHit(isMainAgent);
            }
        }

        public void OnFocusGained(Agent agent)
        {
            if (_agentStatusMap.ContainsKey(agent))
            {
                _agentStatusMap[agent].OnFocusGained();
            }
            else if (agent.IsMount && agent.RiderAgent != null && _agentStatusMap.ContainsKey(agent.RiderAgent))
            {
                _agentStatusMap[agent.RiderAgent].OnFocusGained();
            }
        }

        public void OnFocusLost(Agent agent)
        {
            if (_agentStatusMap.ContainsKey(agent))
            {
                _agentStatusMap[agent].OnFocusLost();
            }
            else if (agent.IsMount && agent.RiderAgent != null && _agentStatusMap.ContainsKey(agent.RiderAgent))
            {
                _agentStatusMap[agent.RiderAgent].OnFocusLost();
            }
        }

        public void OnAgentRemoved(Agent agent, bool isMainAgent)
        {
            if (_agentStatusMap.ContainsKey(agent))
            {
                _agentStatusMap[agent].OnAgentRemoved(isMainAgent);
            }
            else if (agent.IsMount && agent.RiderAgent != null && _agentStatusMap.ContainsKey(agent.RiderAgent))
            {
                _agentStatusMap[agent.RiderAgent].OnAgentRemoved(isMainAgent);
            }
        }

        public void OnAgentCreated(Agent agent)
        {
            if (agent.Team != null)
            {
                AddAgentToMap(agent);
            }
            else
            {
                _newAgents.Add(agent);
            }
        }

        private void AddAgentToMap(Agent agent)
        {
            if (!_agentStatusMap.ContainsKey(agent) && !agent.IsMainAgent && ((AgentStatusBarConfig.AllyHealthBars && agent.Team.IsPlayerAlly) || (AgentStatusBarConfig.EnemyHealthBars && !agent.Team.IsPlayerAlly) || (AgentStatusBarConfig.HeroHealthBars && agent.IsHero)))
            {
                AgentStatusVM asvm = new AgentStatusVM(agent);
                _agentStatusMap.Add(agent, asvm);
                _agentStatusList.Add(asvm);
            }
        }
    }
}