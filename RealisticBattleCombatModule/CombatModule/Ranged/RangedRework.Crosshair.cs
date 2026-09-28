using HarmonyLib;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace RBMCombat
{
    public partial class RangedRework
    {
        /// <summary>
        /// Vanilla MissionGauntletCrosshair.GetShouldCrosshairBeVisible hides the reticle for any crossbow
        /// whose ReloadPhase is below ReloadPhaseCount. A repeating crossbow (ammo_limit > 1) drops into
        /// that state after its first shot while bolts are still in the magazine and it can keep firing,
        /// so the reticle stayed hidden until a full reload. Keep it visible while a bolt is loaded.
        /// Patched by name because RBMCombat does not reference TaleWorlds.MountAndBlade.GauntletUI.
        /// </summary>
        [HarmonyPatch]
        private class RepeatingCrossbowCrosshair
        {
            private static MethodInfo _getShouldCrosshairBeVisible;
            private static MethodInfo _getShouldArrowsBeVisible;

            private static bool Prepare()
            {
                System.Type crosshair = AccessTools.TypeByName("TaleWorlds.MountAndBlade.GauntletUI.Mission.MissionGauntletCrosshair");
                if (crosshair == null)
                {
                    return false;
                }
                _getShouldCrosshairBeVisible = AccessTools.Method(crosshair, "GetShouldCrosshairBeVisible");
                _getShouldArrowsBeVisible = AccessTools.Method(crosshair, "GetShouldArrowsBeVisible");
                return _getShouldCrosshairBeVisible != null && _getShouldArrowsBeVisible != null;
            }

            private static MethodBase TargetMethod()
            {
                return _getShouldCrosshairBeVisible;
            }

            private static void Postfix(object __instance, ref bool __result)
            {
                if (__result || !BannerlordConfig.DisplayTargetingReticule)
                {
                    return;
                }
                Agent mainAgent = Mission.Current?.MainAgent;
                if (mainAgent == null)
                {
                    return;
                }
                MissionWeapon wielded = mainAgent.WieldedWeapon;
                if (wielded.IsEmpty || wielded.CurrentUsageItem.WeaponClass != WeaponClass.Crossbow)
                {
                    return;
                }
                if (wielded.MaxAmmo <= 1 || wielded.Ammo <= 0)
                {
                    return;
                }
                // Remaining vanilla gates (mission mode, custom camera, mouse cursor, suspended view).
                __result = (bool)_getShouldArrowsBeVisible.Invoke(__instance, null);
            }
        }
    }
}
