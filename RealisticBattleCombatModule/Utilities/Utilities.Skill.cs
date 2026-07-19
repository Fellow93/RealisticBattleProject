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

        public static float GetEffectiveSkillWithDR(int effectiveSkill)
        {
            float effectiveSkillWithDR = 0f;
            effectiveSkillWithDR = (600f / (600f + effectiveSkill)) * (float)effectiveSkill;

            //float oneskillStep = 25f;
            //int skillSteps = MathF.Floor(effectiveSkill / 25f);
            //for(int i = 1; i <= skillSteps; i++)
            //{
            //    effectiveSkillWithDR = MathF.Pow(i * oneskillStep, 1f - ((i-1)/100f));
            //}
            return effectiveSkillWithDR;
        }
    }
}
