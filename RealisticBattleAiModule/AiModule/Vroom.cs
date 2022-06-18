using HarmonyLib;
using System;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace RealisticBattleAiModule.AiModule
{
    public class Vroom
    {
        [HarmonyPatch(typeof(Mission))]
        [HarmonyPatch("OnTick")]
        class OnTickPatch
        {
            static void Postfix(float dt)
            {
                if (Mission.Current == null)
                {
                    return;
                }
                try
                {
                    if (ScreenManager.TopScreen != null && (Mission.Current.IsFieldBattle || Mission.Current.IsSiegeBattle))
                    {
                        MissionScreen missionScreen = ScreenManager.TopScreen as MissionScreen;
                        if (missionScreen != null && missionScreen.InputManager != null && missionScreen.InputManager.IsControlDown() && missionScreen.InputManager.IsKeyPressed(InputKey.V))
                        {
                            Mission.Current.SetFastForwardingFromUI(!Mission.Current.IsFastForward);
                            InformationManager.DisplayMessage(new InformationMessage("Vroom = " + Mission.Current.IsFastForward, Color.FromUint(4282569842u)));
                        }
                    }
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
