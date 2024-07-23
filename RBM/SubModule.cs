using HarmonyLib;
using RBMAI;
using RBMCombat;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace RBM
{
    public static class HarmonyModules
    {
        public static Harmony rbmaiHarmony = new Harmony("com.rbmai");
        public static Harmony rbmcombatHarmony = new Harmony("com.rbmcombat");
        public static Harmony rbmHarmony = new Harmony("com.rbmmain");
    }

    public class SubModule : MBSubModuleBase
    {
        public static string ModuleId = "ADODRBM";

        public static void ApplyHarmonyPatches()
        {
            UnpatchAllRBM();
            HarmonyModules.rbmHarmony.PatchAll();
            if (RBMConfig.RBMConfig.rbmAiEnabled)
            {
                RBMAiPatcher.FirstPatch(ref HarmonyModules.rbmaiHarmony);
            }
            else
            {
                HarmonyModules.rbmaiHarmony.UnpatchAll(HarmonyModules.rbmaiHarmony.Id);
            }
            if (RBMConfig.RBMConfig.rbmCombatEnabled)
            {
                RBMCombatPatcher.DoPatching(ref HarmonyModules.rbmcombatHarmony);
            }
            else
            {
                HarmonyModules.rbmcombatHarmony.UnpatchAll(HarmonyModules.rbmcombatHarmony.Id);
            }
        }

        public static void UnpatchAllRBM()
        {
            RBMAiPatcher.patched = false;
            HarmonyModules.rbmHarmony.UnpatchAll(HarmonyModules.rbmHarmony.Id);
            HarmonyModules.rbmaiHarmony.UnpatchAll(HarmonyModules.rbmaiHarmony.Id);
            HarmonyModules.rbmcombatHarmony.UnpatchAll(HarmonyModules.rbmcombatHarmony.Id);
        }

        protected override void OnSubModuleLoad()
        {
            RBMConfig.RBMConfig.LoadConfig();

        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            RBMConfig.RBMConfig.LoadConfig();
            ApplyHarmonyPatches();
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            Game.Current.GameTextManager.LoadGameTexts();
            if (RBMConfig.RBMConfig.developerMode)
            {
                mission.AddMissionBehavior((MissionBehavior)(object)new BattleStatsLogic());
            }
            if (RBMConfig.RBMConfig.rbmCombatEnabled)
            {
                if (RBMConfig.RBMConfig.armorStatusUIEnabled)
                {
                    mission.AddMissionBehavior((MissionBehavior)(object)new PlayerArmorStatus());
                }
            }
            if (RBMConfig.RBMConfig.rbmAiEnabled)
            {
                if (RBMConfig.RBMConfig.postureEnabled && RBMConfig.RBMConfig.postureGUIEnabled)
                {
                    mission.AddMissionBehavior((MissionBehavior)(object)new PostureVisualLogic());
                }
                mission.AddMissionBehavior((MissionBehavior)(object)new SiegeArcherPoints());
                mission.AddMissionBehavior((MissionBehavior)(object)new PostureLogic());
            }
            else
            {
                if (mission.GetMissionBehavior<SiegeArcherPoints>() != null)
                {
                    mission.RemoveMissionBehavior(mission.GetMissionBehavior<SiegeArcherPoints>());
                }
                if (mission.GetMissionBehavior<PostureVisualLogic>() != null)
                {
                    mission.RemoveMissionBehavior(mission.GetMissionBehavior<PostureVisualLogic>());
                }
                if (mission.GetMissionBehavior<PostureLogic>() != null)
                {
                    mission.RemoveMissionBehavior(mission.GetMissionBehavior<PostureLogic>());
                }
            }
            base.OnMissionBehaviorInitialize(mission);
        }
    }
}