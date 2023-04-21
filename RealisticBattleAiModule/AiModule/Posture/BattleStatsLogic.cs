using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using static RBMAI.Tactics;

namespace RBMAI
{
    public class BattleStatsLogic : MissionLogic
    {
        public BattleStatsVM _dataSource;

        private GauntletLayer _gauntletLayer;

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
            MissionScreen missionScreen = TaleWorlds.ScreenSystem.ScreenManager.TopScreen as MissionScreen;
            _dataSource = new BattleStatsVM();
            _gauntletLayer = new GauntletLayer(-1, "GauntletLayer");
            missionScreen.AddLayer(_gauntletLayer);
            _gauntletLayer.LoadMovie("BattleStats", (ViewModel)_dataSource);
        }

        public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent, in MissionWeapon attackerWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
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
            //InformationManager.DisplayMessage(new InformationMessage("ATK ARC:" + archersDamageDone));
            //InformationManager.DisplayMessage(new InformationMessage("ATK HA :" + haDamageDone));
            //InformationManager.DisplayMessage(new InformationMessage("ATK CAV:" + cavDamageDone));
            //InformationManager.DisplayMessage(new InformationMessage("ATK INF:" + infDamageDone));
            //InformationManager.DisplayMessage(new InformationMessage("DEF ARC:" + archersDamageDone));
            //InformationManager.DisplayMessage(new InformationMessage("DEF HA :" + haDamageDone));
            //InformationManager.DisplayMessage(new InformationMessage("DEF CAV:" + cavDamageDone));
            //InformationManager.DisplayMessage(new InformationMessage("DEF INF:" + infDamageDone));
            _dataSource.Atkarc = "ATK ARC:" + atkarc;
            _dataSource.Atkha = "ATK HA :" + atkha;
            _dataSource.Atkcav = "ATK CAV:" + atkcav;
            _dataSource.Atkinf = "ATK INF:" + atkinf;
            _dataSource.Defarc = "DEF ARC:" + defarc;
            _dataSource.Defha = "DEF HA :" + defha;
            _dataSource.Defcav = "DEF CAV:" + defcav;
            _dataSource.Definf = "DEF INF:" + definf;
        }
    }
}