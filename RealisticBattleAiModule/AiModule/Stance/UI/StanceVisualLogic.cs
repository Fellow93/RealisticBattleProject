using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;

namespace RBMAI
{
    public class StanceVisualLogic : MissionLogic
    {
        public int timer = 0;

        public StanceVisualVM _dataSource;

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
            MissionScreen missionScreen = TaleWorlds.ScreenSystem.ScreenManager.TopScreen as MissionScreen;
            _dataSource = new StanceVisualVM();
            _gauntletLayer = new GauntletLayer("GauntletLayer", -1);
            missionScreen.AddLayer(_gauntletLayer);
            _gauntletLayer.LoadMovie("CombatUI", (ViewModel)_dataSource);
            _dataSource.ShowPlayerPostureStatus = true;
            AgentStances.postureVisual = this;
        }

        public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent, in MissionWeapon attackerWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
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

                Stance stance = null;
                if (AgentStances.values.TryGetValue(affectedAgent, out stance))
                {
                    _dataSource.EnemyPosture = (int)stance.posture;
                    _dataSource.EnemyPostureMax = (int)stance.maxPosture;

                    _dataSource.EnemyStamina = (int)stance.stamina;
                    _dataSource.EnemyStaminaMax = (int)stance.maxStamina;
                }

                if (affectedAgent.IsMount)
                {
                    _dataSource.EnemyName = affectedAgent.RiderAgent?.Name + " (" + new TextObject("{=mountnoun}Mount").ToString() + ")";
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