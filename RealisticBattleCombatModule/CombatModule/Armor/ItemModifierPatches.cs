using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Core.ArmorComponent;

namespace RBMCombat
{
    [HarmonyPatch(typeof(ItemModifier))]
    [HarmonyPatch("ModifyArmor")]
    internal class ModifyArmorPatch
    {
        private static int ModifyFactor(int baseValue, float factor)
        {
            if (baseValue == 0)
            {
                return 0;
            }
            if (!MBMath.ApproximatelyEquals(factor, 0f))
            {
                baseValue = ((factor < 1f) ? MathF.Ceiling(factor * (float)baseValue) : MathF.Floor(factor * (float)baseValue));
            }
            return baseValue;
        }

        private static bool Prefix(ref int armorValue, ref int __result, ref ItemModifier __instance)
        {
            float calculatedModifier = 1f + (__instance.Armor / 100f);
            int result = ModifyFactor(armorValue, calculatedModifier);
            __result = MBMath.ClampInt(result, 1, result);
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemModifier))]
    [HarmonyPatch("ModifyDamage")]
    internal class ModifyModifyDamagePatch
    {
        private static int ModifyFactor(int baseValue, float factor)
        {
            if (baseValue == 0)
            {
                return 0;
            }
            if (!MBMath.ApproximatelyEquals(factor, 0f))
            {
                baseValue = ((factor < 1f) ? MathF.Ceiling(factor * (float)baseValue) : MathF.Floor(factor * (float)baseValue));
            }
            return baseValue;
        }

        private static bool Prefix(ref int baseDamage, ref int __result, ref ItemModifier __instance)
        {
            float calculatedModifier = 1f + (__instance.Damage / 100f);
            int result = ModifyFactor(baseDamage, calculatedModifier);
            __result = MBMath.ClampInt(result, 1, result);
            return false;
        }
    }
}
