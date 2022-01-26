using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Engine.Screens;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screen;

namespace RealisticBattleAiModule.AiModule.Posture
{
	public class PostureVisualLogic : MissionLogic
	{
		public int timer = 0;

		public PostureVisualVM _dataSource;

		private GauntletLayer _gauntletLayer;
		public Agent affectedAgent = null;

		public int DisplayTime
		{
			get
			{
				int result = 600;
				return result;
			}
		}

		public bool IsEnabled
		{
			get
			{
				bool result = true;
				return result;
			}
		}

		public Agent PlayerTarger { get; set; }

		public override void OnMissionTick(float dt)
		{
			if (timer == 0)
			{
				_dataSource.ShowEnemyStatus = false;
			}
			else
			{
				timer--;
			}
		}

		public override void AfterStart()
		{
			MissionScreen missionScreen = ScreenManager.TopScreen as MissionScreen;
			_dataSource = new PostureVisualVM();
			_gauntletLayer = new GauntletLayer(-1, "GauntletLayer");
			missionScreen.AddLayer(_gauntletLayer);
			_gauntletLayer.LoadMovie("CombatUI", (ViewModel)_dataSource);
			_dataSource.ShowPlayerPostureStatus = true;
			AgentPostures.postureVisual = this;
		}

		public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent, int damage, in MissionWeapon affectorWeapon)
		{
			//this.OnAgentHit(affectedAgent, affectorAgent, damage, affectorWeapon);
			if (!IsEnabled || affectorAgent == null || affectedAgent == null || Agent.Main == null)
			{
				return;
			}
			if (affectorAgent.IsPlayerControlled || affectorAgent == Agent.Main.MountAgent)
			{
				if (affectedAgent.Health == 0f)
				{
					timer = 200;
				}
				else
				{
					timer = DisplayTime;
				}

				PlayerTarger = affectedAgent;
				this.affectedAgent = affectedAgent;
				_dataSource.ShowEnemyStatus = true;
				_dataSource.EnemyHealth = (int)affectedAgent.Health;
				_dataSource.EnemyHealthMax = (int)affectedAgent.HealthLimit;

				Posture posture = null;
				if(AgentPostures.values.TryGetValue(affectedAgent, out posture))
				{
					_dataSource.EnemyPosture = (int)posture.posture;
					_dataSource.EnemyPostureMax = (int)posture.maxPosture;
				}

				if (affectedAgent.IsMount)
				{
					_dataSource.EnemyName = affectedAgent.RiderAgent?.Name + " (Mount)";
				}
				else
				{
					_dataSource.EnemyName = affectedAgent.Name;
				}
			}
			if (affectedAgent == PlayerTarger)
			{
				_dataSource.EnemyHealth = (int)affectedAgent.Health;
				_dataSource.EnemyHealthMax = (int)affectedAgent.HealthLimit;
			}
		}
	}
}

