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

        private static readonly PropertyInfo MissileSpeedProperty = typeof(WeaponComponentData).GetProperty("MissileSpeed");
        private static readonly PropertyInfo SwingSpeedProperty = typeof(WeaponComponentData).GetProperty("SwingSpeed");
        private static readonly PropertyInfo ThrustSpeedProperty = typeof(WeaponComponentData).GetProperty("ThrustSpeed");
        private static readonly PropertyInfo HandlingProperty = typeof(WeaponComponentData).GetProperty("Handling");
        private static readonly PropertyInfo SiegeShootingDirectionProperty = typeof(RangedSiegeWeapon).GetProperty("ShootingDirection", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly PropertyInfo SiegeShootingSpeedProperty = typeof(RangedSiegeWeapon).GetProperty("ShootingSpeed", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly PropertyInfo SiegeProjectileProperty = typeof(RangedSiegeWeapon).GetProperty("Projectile", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly PropertyInfo SiegeMissileStartPositionProperty = typeof(RangedSiegeWeapon).GetProperty("MissileStartingGlobalPositionForSimulation", BindingFlags.NonPublic | BindingFlags.Instance);
    }
}
