using HarmonyLib;
using JetBrains.Annotations;
using NetworkMessages.FromServer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ItemObject;
using static TaleWorlds.MountAndBlade.Mission;

namespace RBMCombat
{
    public partial class RangedRework
    {
        [HarmonyPatch(typeof(RangedSiegeWeapon))]
        internal class OverrideRangedSiegeWeapon
        {
            [HarmonyPrefix]
            [HarmonyPatch("GetTargetReleaseAngle")]
            private static bool PrefixGetTargetReleaseAngle(RangedSiegeWeapon __instance, ref float __result, Vec3 target, ref string[] ___SkeletonNames, ItemObject ___OriginalMissileItem)
            {
                if (___SkeletonNames != null && ___SkeletonNames.Length > 0 && ___SkeletonNames[0].Contains("ballista"))
                {
                    Vec3 MissileStartingGlobalPositionForSimulation = (Vec3)SiegeMissileStartPositionProperty.GetValue(__instance, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);

                    WeaponStatsData weaponStatsData = new MissionWeapon(___OriginalMissileItem, null, null).GetWeaponStatsDataForUsage(0);
                    __result = Mission.GetMissileVerticalAimCorrection(target - MissileStartingGlobalPositionForSimulation, 60f, ref weaponStatsData, ItemObject.GetAirFrictionConstant(___OriginalMissileItem.PrimaryWeapon.WeaponClass, ___OriginalMissileItem.PrimaryWeapon.WeaponFlags));
                    return false;
                }
                else
                {
                    return true;
                }
            }

            [HarmonyPrefix]
            [HarmonyPatch("ShootProjectileAux")]
            private static bool PrefixShootProjectileAux(ref RangedSiegeWeapon __instance, ref string[] ___SkeletonNames, ref ItemObject missileItem, ref Agent ___LastShooterAgent)
            {
                if (___SkeletonNames != null && ___SkeletonNames.Length > 0 && ___SkeletonNames[0].Contains("trebuchet"))
                {
                    for (int i = 0; i < 7; i++)
                    {
                        Mat3 mat = default(Mat3);

                        mat.f = (Vec3)SiegeShootingDirectionProperty.GetValue(__instance, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);

                        mat.u = Vec3.Up;
                        Mat3 mat2 = mat;
                        mat2.Orthonormalize();
                        float a = MBRandom.RandomFloat * ((float)Math.PI * 2f);
                        mat2.RotateAboutForward(a);
                        float f = 1.5f * MBRandom.RandomFloat;
                        mat2.RotateAboutSide(f.ToRadians());

                        Mat3 identity = Mat3.Identity;

                        ItemObject @object = Game.Current.ObjectManager.GetObject<ItemObject>("grapeshot_projectile");

                        float num = (float)SiegeShootingSpeedProperty.GetValue(__instance, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);

                        num *= MBRandom.RandomFloatRanged(0.95f, 1.05f);
                        identity.f = mat2.f;
                        identity.Orthonormalize();

                        Vec3 ProjectileEntityCurrentGlobalPosition = ((SynchedMissionObject)SiegeProjectileProperty.GetValue(__instance, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null)).GameEntity.GetGlobalFrame().origin;

                        Mission.Current.AddCustomMissile(___LastShooterAgent, new MissionWeapon(@object, null, ___LastShooterAgent.Origin?.Banner, 1), ProjectileEntityCurrentGlobalPosition, identity.f, identity, num, num, addRigidBody: false, __instance);
                    }
                    return false;
                }
                if (___SkeletonNames != null && ___SkeletonNames.Length > 0 && ___SkeletonNames[0].Contains("ballista"))
                {
                    Mat3 mat = default(Mat3);

                    mat.f = (Vec3)SiegeShootingDirectionProperty.GetValue(__instance, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null);

                    mat.u = Vec3.Up;
                    Mat3 mat2 = mat;
                    mat2.Orthonormalize();
                    float a = MBRandom.RandomFloat * ((float)MathF.PI * 2f);
                    mat2.RotateAboutForward(a);
                    float f = 1f * MBRandom.RandomFloat;
                    mat2.RotateAboutSide(f.ToRadians());

                    Mat3 identity = Mat3.Identity;
                    identity.f = mat2.f;
                    identity.Orthonormalize();

                    Vec3 ProjectileEntityCurrentGlobalPosition = ((SynchedMissionObject)SiegeProjectileProperty.GetValue(__instance, BindingFlags.NonPublic | BindingFlags.GetProperty, null, null, null)).GameEntity.GetGlobalFrame().origin;

                    Mission.Current.AddCustomMissile(___LastShooterAgent, new MissionWeapon(missileItem, null, null, 1), ProjectileEntityCurrentGlobalPosition, identity.f, identity, 60f, 60f, addRigidBody: false, __instance);
                    return false;
                }
                else
                {
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(Mangonel))]
        internal class OverrideMangonel
        {
            [HarmonyPrefix]
            [HarmonyPatch("OnTick")]
            private static bool PrefixOnTick(ref Mangonel __instance, ref float ___CurrentReleaseAngle)
            {
                float baseSpeed = 25f;
                float speedIncrease = 1.5f;
                __instance.ProjectileSpeed = baseSpeed + (((___CurrentReleaseAngle * MathF.RadToDeg)) * speedIncrease);

                return true;
            }
        }
    }
}
