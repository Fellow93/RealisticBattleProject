using System;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Library;

namespace RBMAI
{
    public class Stance
    {
        public const float PostureRegenRubberBandStrength = 0f;
        public const float StaminaRegenRubberBandStrength = 2.5f;

        public float posture;
        public float maxPosture = 100f;
        public float postureRegenPerTick = 0.01f;

        public float stamina;
        public float maxStamina = 1500f;
        public float staminaRegenPerTick = 0.01f;

        public Stance()
        {
            this.posture = this.maxPosture;

            this.stamina = this.maxStamina;
        }

        public static void InitializeStamina(Agent agent, ref Stance stance)
        {
            float athleticBase = 1000f;
            int effectiveAthleticSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(agent, DefaultSkills.Athletics);
            float athleticSkillModifier = 500f;
            stance.maxStamina = athleticBase * (1f + 3f * (effectiveAthleticSkill / athleticSkillModifier));
            stance.stamina = athleticBase * (1f + 3f * (effectiveAthleticSkill / athleticSkillModifier));
            stance.staminaRegenPerTick = 0.03f * (1f + 2f * (effectiveAthleticSkill / athleticSkillModifier));

            //face armor effect
            float faceArmor = 0f;
            if (!agent.SpawnEquipment[EquipmentIndex.Head].IsEmpty)
            {
                faceArmor = agent.SpawnEquipment[EquipmentIndex.Head].GetModifiedBodyArmor();
            }
            if (faceArmor >= 30f)
            {
                stance.staminaRegenPerTick *= 0.5f;
            }

            if (agent.IsPlayerControlled)
            {
                stance.maxStamina *= RBMConfig.RBMConfig.playerPostureMultiplier;
                stance.staminaRegenPerTick *= RBMConfig.RBMConfig.playerPostureMultiplier;
            }
        }

        public static void InitializePosture(Agent agent, ref Stance stance)
        {
            float oldPosture = stance.posture;
            float oldMaxPosture = stance.maxPosture;
            float oldPosturePercentage = oldPosture / oldMaxPosture;

            int usageIndex = 0;
            EquipmentIndex slotIndex = agent.GetPrimaryWieldedItemIndex();
            if (slotIndex != EquipmentIndex.None)
            {
                usageIndex = agent.Equipment[slotIndex].CurrentUsageIndex;
                int usageCount = agent.Equipment[slotIndex].Item?.Weapons?.Count ?? 0;
                if (usageCount > 0)
                {
                    usageIndex = MBMath.ClampInt(usageIndex, 0, usageCount - 1);
                }

                WeaponComponentData wcd = agent.Equipment[slotIndex].GetWeaponComponentDataForUsage(usageIndex);
                SkillObject weaponSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(wcd.WeaponClass);
                int effectiveWeaponSkill = 0;
                if (weaponSkill != null)
                {
                    effectiveWeaponSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(agent, weaponSkill);
                }

                float basePosture = 30f;
                float athleticBase = 20f;
                float weaponSkillBase = 80f;
                float strengthSkillModifier = 500f;
                float weaponSkillModifier = 500f;
                float athleticRegenBase = 0.016f;
                float weaponSkillRegenBase = 0.064f;
                float baseModifier = 1f;

                stance.maxPosture = basePosture;
                if (agent.HasMount)
                {
                    int effectiveRidingSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(agent, DefaultSkills.Riding);
                    stance.maxPosture += (athleticBase * (baseModifier + (effectiveRidingSkill / strengthSkillModifier))) + (weaponSkillBase * (baseModifier + (effectiveWeaponSkill / weaponSkillModifier)));
                    stance.postureRegenPerTick = (athleticRegenBase * (baseModifier + (effectiveRidingSkill / strengthSkillModifier))) + (weaponSkillRegenBase * (baseModifier + (effectiveWeaponSkill / weaponSkillModifier)));
                }
                else
                {
                    int effectiveAthleticSkill = MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(agent, DefaultSkills.Athletics);
                    stance.maxPosture += (athleticBase * (baseModifier + (effectiveAthleticSkill / strengthSkillModifier))) + (weaponSkillBase * (baseModifier + (effectiveWeaponSkill / weaponSkillModifier)));
                    stance.postureRegenPerTick = (athleticRegenBase * (baseModifier + (effectiveAthleticSkill / strengthSkillModifier))) + (weaponSkillRegenBase * (baseModifier + (effectiveWeaponSkill / weaponSkillModifier)));
                }

                if (agent.IsPlayerControlled)
                {
                    stance.maxPosture *= RBMConfig.RBMConfig.playerPostureMultiplier;
                    stance.postureRegenPerTick *= RBMConfig.RBMConfig.playerPostureMultiplier;
                }

                //armor weight effect
                float armorWeight = Math.Max(0f, agent.SpawnEquipment.GetTotalWeightOfArmor(true) - 5f);
                stance.maxPosture += armorWeight;

                stance.posture = stance.maxPosture * oldPosturePercentage;
            }
        }

        public void reduceStamina(float value)
        {
            this.stamina = Math.Max(0f, this.stamina - value);
        }

        public void addStamina(float value)
        {
            this.stamina = Math.Min(maxStamina, this.stamina + value);
        }

        public void reducePosture(float value)
        {
            this.posture = Math.Max(0f, this.posture - value);
        }

        public void addPosture(float value)
        {
            this.posture = Math.Min(maxPosture, this.posture + value);
        }

        public float calcualteRubberBandFactor(float current, float max, float rubberBandStrength)
        {
            float deficit = 1f - (current / max);
            return ((float)Math.Pow(1f + deficit, rubberBandStrength));
        }

        public void tickStaminaRegen(int tickCount = 30, float multiplier = 1f)
        {
            float rubberBandFactor = calcualteRubberBandFactor(this.stamina, this.maxStamina, StaminaRegenRubberBandStrength);
            float value = this.staminaRegenPerTick;
            value *= tickCount;
            value *= multiplier;
            value *= rubberBandFactor;
            addStamina(value);
        }

        public void tickPostureRegen(int tickCount = 30, float multiplier = 1f)
        {
            float rubberBandFactor = calcualteRubberBandFactor(this.posture, this.maxPosture, PostureRegenRubberBandStrength);
            float value = this.postureRegenPerTick;
            value *= tickCount;
            value *= multiplier;
            value *= rubberBandFactor;
            addPosture(value);
        }

        public Stance(float maxPosture, float regenPerTick, float maxStamina, float staminaRegenPerTick)
        {
            this.maxPosture = maxPosture;
            this.postureRegenPerTick = regenPerTick;
            this.posture = this.maxPosture;

            this.maxStamina = maxStamina;
            this.staminaRegenPerTick = staminaRegenPerTick;
            this.stamina = this.maxStamina;
        }
    }
}
