using HarmonyLib;
using SandBox.Missions.MissionLogics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.GauntletUI.Mission.Singleplayer;
using TaleWorlds.MountAndBlade.ViewModelCollection.HUD.FormationMarker;
using static TaleWorlds.Core.ItemObject;

namespace RBMAI
{
    internal static class FormationMarkerPatches
    {
        [HarmonyPatch(typeof(MissionFormationMarkerTargetVM))]
        [HarmonyPatch("Refresh")]
        private class OverrideRefresh
        {
            private static string chooseIcon(Formation formation)
            {
                if (formation != null)
                {
                    if (formation.QuerySystem.IsInfantryFormation)
                    {
                        return TargetIconType.Special_Swordsman.ToString();
                    }
                    if (formation.QuerySystem.IsRangedFormation)
                    {
                        return TargetIconType.Archer_Heavy.ToString();
                    }
                    if (formation.QuerySystem.IsRangedCavalryFormation)
                    {
                        return TargetIconType.HorseArcher_Light.ToString();
                    }
                    if (formation.QuerySystem.IsCavalryFormation && !RBMAI.Utilities.CheckIfMountedSkirmishFormation(formation, 0.6f))
                    {
                        return TargetIconType.Cavalry_Light.ToString();
                    }
                    if (formation.QuerySystem.IsCavalryFormation && RBMAI.Utilities.CheckIfMountedSkirmishFormation(formation, 0.6f))
                    {
                        return TargetIconType.Special_JavelinThrower.ToString();
                    }
                }
                return TargetIconType.None.ToString();
            }

            private static void Postfix(MissionFormationMarkerTargetVM __instance)
            {
                __instance.FormationType = chooseIcon(__instance.Formation);
            }
        }

        // Stabilizes the in-battle formation banner marker. Vanilla anchors each marker to
        // Formation.CachedMedianPosition -- the world position of whichever single soldier is
        // currently closest to the formation's centroid (GetMedianAgent). As units shuffle that
        // "median" soldier flips, so the banner snaps erratically from man to man; the cached
        // position also only refreshes on a ~75-125ms timer, which makes it step. We re-anchor
        // to SmoothedAverageUnitPosition, which the engine already lerps toward the formation's
        // true centroid every tick, so it neither jumps between soldiers nor steps. The median
        // is still used as the navmesh carrier for the ground height, matching the idiom native
        // uses in FormationQuerySystem. Distance/DistanceText and the count are left to native.
        [HarmonyPatch(typeof(MissionGauntletFormationMarker))]
        [HarmonyPatch("UpdateMarkerPositions")]
        private class OverrideMarkerPositions
        {
            private static readonly Vec3 heightOffset = new Vec3(0f, 0f, 3f, -1f);

            private static void Postfix(MissionGauntletFormationMarker __instance)
            {
                var camera = __instance.MissionScreen?.CombatCamera;
                MissionFormationMarkerVM ds = Traverse.Create(__instance).Field("_dataSource").GetValue<MissionFormationMarkerVM>();
                if (camera == null || ds == null)
                {
                    return;
                }

                foreach (MissionFormationMarkerTargetVM target in ds.Targets)
                {
                    Formation f = target.Formation;
                    if (f == null)
                    {
                        continue;
                    }
                    WorldPosition wp = f.CachedMedianPosition; // carries the navmesh face for the ground Z
                    if (!wp.IsValid)
                    {
                        continue;
                    }
                    // invalid until the formation's first tick has run
                    Vec2 anchor = f.SmoothedAverageUnitPosition.IsValid ? f.SmoothedAverageUnitPosition : f.CachedAveragePosition;
                    if (!anchor.IsValid)
                    {
                        continue;
                    }

                    wp.SetVec2(anchor);
                    float x = 0f, y = 0f, w = 0f;
                    MBWindowManager.WorldToScreen(camera, wp.GetGroundVec3() + heightOffset, ref x, ref y, ref w);
                    if (!TaleWorlds.Library.MathF.IsValidValue(w) || !TaleWorlds.Library.MathF.IsValidValue(x) || !TaleWorlds.Library.MathF.IsValidValue(y))
                    {
                        continue;
                    }

                    target.WSign = (w < 0f) ? -1 : 1;
                    target.ScreenPosition = new Vec2(x, y);
                }
            }
        }
    }
}
