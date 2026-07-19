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
