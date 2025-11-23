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
            _gauntletLayer = new GauntletLayer("GauntletLayer" ,- 1);
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