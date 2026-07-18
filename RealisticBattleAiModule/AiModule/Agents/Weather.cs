using HarmonyLib;
using Helpers;
using JetBrains.Annotations;
using SandBox.GameComponents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.MountAndBlade.ArrangementOrder;

namespace RBMAI
{
    public static partial class AgentAi
    {
        private static bool NeutralizeWeatherEffects()
        {
            Scene scene = Mission.Current.Scene;
            if (scene != null)
            {
                Mission.Current.SetBowMissileSpeedModifier(1f);
                Mission.Current.SetCrossbowMissileSpeedModifier(1f);
                Mission.Current.SetMissileRangeModifier(1f);
            }

            return false;
        }

        [HarmonyPatch(typeof(CustomBattleApplyWeatherEffectsModel))]
        [HarmonyPatch("ApplyWeatherEffects")]
        public class OverrideApplyWeatherEffectsCustomBattle
        {
            private static bool Prefix() => NeutralizeWeatherEffects();
        }

        [HarmonyPatch(typeof(SandboxApplyWeatherEffectsModel))]
        [HarmonyPatch("ApplyWeatherEffects")]
        public class OverrideApplyWeatherEffectsSandbox
        {
            private static bool Prefix() => NeutralizeWeatherEffects();
        }
    }
}
