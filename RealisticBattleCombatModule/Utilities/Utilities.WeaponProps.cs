using RBMConfig;
using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ArmorComponent;

namespace RBMCombat
{
    public static partial class Utilities
    {
        // WeaponClass.ToString() for every value up to the enum's highest, built once: the blow math keys its
        // per-class tables by these names on every hit, and an enum ToString allocates. Anything outside the
        // table falls back to ToString(), so the name is always the same string.
        private static readonly string[] weaponClassNames = BuildWeaponClassNames();

        private static string[] BuildWeaponClassNames()
        {
            int max = 0;
            foreach (WeaponClass value in Enum.GetValues(typeof(WeaponClass)))
            {
                max = Math.Max(max, (int)value);
            }
            string[] names = new string[max + 1];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = ((WeaponClass)i).ToString();
            }
            return names;
        }

        public static string WeaponClassName(WeaponClass weaponClass)
        {
            int index = (int)weaponClass;
            return index >= 0 && index < weaponClassNames.Length ? weaponClassNames[index] : weaponClass.ToString();
        }

        public static float getSwingDamageFactor(WeaponComponentData wcd, ItemModifier itemModifier)
        {
            if (itemModifier == null)
            {
                return wcd.SwingDamageFactor;
            }
            else
            {
                float factorBonus = (itemModifier.ModifyDamage(100) - 100) / 100f;
                return wcd.SwingDamageFactor + factorBonus;
            }
        }

        public static float getThrustDamageFactor(WeaponComponentData wcd, ItemModifier itemModifier)
        {
            if (itemModifier == null)
            {
                return wcd.ThrustDamageFactor;
            }
            else
            {
                float factorBonus = (itemModifier.ModifyDamage(100) - 100) / 100f;
                return wcd.ThrustDamageFactor + factorBonus;
            }
        }
    }
}
