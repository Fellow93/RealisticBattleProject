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
        public static float CalculateSkillModifier(int relevantSkillLevel)
        {
            return MBMath.ClampFloat((float)relevantSkillLevel / 250f, 0f, 1f);
        }

        public static float CalculateSkillModifier(float relevantSkillLevel)
        {
            return MBMath.ClampFloat(relevantSkillLevel / 250f, 0f, 1f);
        }

        // Body lives in RBMConfig.MissileBallistics so RBMAI's reach gate uses the same sling speed.
        public static float GetEffectiveSkillWithDR(int effectiveSkill)
        {
            return MissileBallistics.EffectiveSkillWithDR(effectiveSkill);
        }
    }
}
