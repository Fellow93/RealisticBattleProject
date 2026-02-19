using HarmonyLib;
using System;
using System.Collections.Generic;
using TaleWorlds.MountAndBlade.View.CustomBattle;

namespace RBM
{
    internal class CustomBattlePatches
    {
        [HarmonyPatch(typeof(CustomBattleFactory))]
        [HarmonyPatch("StartCustomBattle")]
        private class StartCustomBattlePatch
        {
            private static bool Prefix(List<Type> ____providers)
            {
                if (____providers.Count > 1)
                {
                    (Activator.CreateInstance(____providers[1]) as ICustomBattleProvider).StartCustomBattle();
                    return false;
                }
                if (____providers.Count > 0)
                {
                    (Activator.CreateInstance(____providers[0]) as ICustomBattleProvider).StartCustomBattle();
                    return false;
                }
                return false;
            }
        }
    }
}