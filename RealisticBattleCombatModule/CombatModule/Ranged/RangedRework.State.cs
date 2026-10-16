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
        public static Dictionary<TextObject, int> originalItemSwingSpeed = new Dictionary<TextObject, int> { };
        public static Dictionary<TextObject, int> originalItemThrustSpeed = new Dictionary<TextObject, int> { };
        public static Dictionary<TextObject, int> originalItemHandling = new Dictionary<TextObject, int> { };
        public static Dictionary<string, RangedWeaponStats> rangedWeaponStats = new Dictionary<string, RangedWeaponStats>(new RangedWeaponStatsComparer());
        public static Dictionary<string, MissionWeapon> rangedWeaponMW = new Dictionary<string, MissionWeapon> { };

        // Key for rangedWeaponStats / rangedWeaponMW: item + modifier StringId, never the localized display
        // name (two different items can share a name, and it changes with the language). Both dictionaries are
        // cleared at EndMission, after the draw weights have been restored.
        internal static string GetRangedWeaponKey(MissionWeapon missionWeapon)
        {
            return missionWeapon.Item?.StringId + "|" + (missionWeapon.ItemModifier?.StringId ?? "");
        }

        private static readonly PropertyInfo MissileSpeedProperty = typeof(WeaponComponentData).GetProperty("MissileSpeed");

        // MissileSpeed's private setter, bound once as an open-instance delegate (WeaponComponentData is a class)
        // for the per-shot writes, instead of a PropertyInfo.SetValue with a boxed int.
        private static readonly Action<WeaponComponentData, int> SetMissileSpeed = BindMissileSpeedSetter();

        private static Action<WeaponComponentData, int> BindMissileSpeedSetter()
        {
            MethodInfo setter = MissileSpeedProperty?.GetSetMethod(true);
            if (setter != null)
            {
                try
                {
                    return (Action<WeaponComponentData, int>)Delegate.CreateDelegate(typeof(Action<WeaponComponentData, int>), setter);
                }
                catch (ArgumentException)
                {
                    // Type changed (game update): the reflection call below, so only this write fails, as before,
                    // never the whole class's type initializer.
                }
            }
            // Property or setter gone or changed (game update): fail exactly as the old reflection call did.
            return (weapon, value) => MissileSpeedProperty.SetValue(weapon, value, BindingFlags.NonPublic | BindingFlags.SetProperty, null, null, null);
        }
        private static readonly PropertyInfo SwingSpeedProperty = typeof(WeaponComponentData).GetProperty("SwingSpeed");
        private static readonly PropertyInfo ThrustSpeedProperty = typeof(WeaponComponentData).GetProperty("ThrustSpeed");
        private static readonly PropertyInfo HandlingProperty = typeof(WeaponComponentData).GetProperty("Handling");
        private static readonly PropertyInfo SiegeShootingDirectionProperty = typeof(RangedSiegeWeapon).GetProperty("ShootingDirection", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly PropertyInfo SiegeShootingSpeedProperty = typeof(RangedSiegeWeapon).GetProperty("ShootingSpeed", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly PropertyInfo SiegeProjectileProperty = typeof(RangedSiegeWeapon).GetProperty("Projectile", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly PropertyInfo SiegeMissileStartPositionProperty = typeof(RangedSiegeWeapon).GetProperty("MissileStartingGlobalPositionForSimulation", BindingFlags.NonPublic | BindingFlags.Instance);
    }
}
