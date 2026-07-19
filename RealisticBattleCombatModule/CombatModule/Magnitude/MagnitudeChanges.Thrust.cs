using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.CampaignSystem.ViewModelCollection.Inventory;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace RBMCombat
{
    public static partial class MagnitudeChanges
    {
        [HarmonyPatch(typeof(CombatStatCalculator))]
        [HarmonyPatch("CalculateStrikeMagnitudeForPassiveUsage")]
        private class ChangeLanceDamage
        {
            private static bool Prefix(float weaponWeight, float extraLinearSpeed, ref float __result)
            {
                __result = CalculateStrikeMagnitudeForThrust(0f, weaponWeight, extraLinearSpeed, isThrown: false);
                return false;
            }

            private static float CalculateStrikeMagnitudeForThrust(float thrustWeaponSpeed, float weaponWeight, float extraLinearSpeed, bool isThrown)
            {
                float num = extraLinearSpeed * 1f; // because cav in the game is roughly 50% faster than it should be
                float num2 = 0.5f * weaponWeight * num * num * RBMConfig.RBMConfig.ThrustMagnitudeModifier; // lances need to have 3 times more damage to be preferred over maces
                return num2;
            }
        }

        [HarmonyPatch(typeof(CombatStatCalculator))]
        [HarmonyPatch("CalculateStrikeMagnitudeForThrust")]
        private class CalculateStrikeMagnitudeForThrustPatch
        {
            private static bool Prefix(float thrustWeaponSpeed, float weaponWeight, float extraLinearSpeed, bool isThrown, ref float __result)
            {
                float combinedSpeed = MBMath.ClampFloat(thrustWeaponSpeed, 4f, 6f) + extraLinearSpeed;
                if (combinedSpeed > 0f)
                {
                    float kineticEnergy = 0.5f * weaponWeight * combinedSpeed * combinedSpeed;
                    float mixedEnergy = 0.5f * (weaponWeight + 1.5f) * combinedSpeed * combinedSpeed;
                    float baselineEnergy = 0.5f * 8f * combinedSpeed * combinedSpeed;
                    //float basedamage = 0.5f * (weaponWeight + 4.5f) * combinedSpeed * combinedSpeed;

                    //float basedamage = 120f;
                    //if (mixedEnergy > 120f)
                    //{
                    //    basedamage = mixedEnergy;
                    //}

                    //float handBonus = 0.5f * (weaponWeight + 1.5f) * combinedSpeed * combinedSpeed;
                    //float handLimit = 120f;
                    //if (handBonus > handLimit)
                    //{
                    //    handBonus = handLimit;
                    //}
                    //float thrust = handBonus;
                    //if (kineticEnergy > handLimit)
                    //{
                    //    thrust = kineticEnergy;
                    //}
                    //else if (basedamage > 180f)
                    //{
                    //    basedamage = 180f;
                    //}
                    //float thrust = basedamage;
                    //if (kineticEnergy > basedamage)
                    //{
                    //    thrust = kineticEnergy;
                    //}

                    //if (thrust > 200f)
                    //{
                    //    thrust = 200f;
                    //}
                    float thrust = baselineEnergy;
                    __result = thrust * RBMConfig.RBMConfig.ThrustMagnitudeModifier;
                    return false;
                }
                __result = 0f;
                return false;
            }
        }
    }
}
